﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Async;
using static System.Diagnostics.Debug;

namespace AI4E.Coordination
{
    public sealed class SessionManager : ISessionManager
    {
        private readonly ISessionStorage _storage;
        private readonly IStoredSessionManager _storedSessionManager;
        private readonly IDateTimeProvider _dateTimeProvider;

        private readonly Dictionary<string, Task> _sessionTerminationCache = new Dictionary<string, Task>();

        public SessionManager(ISessionStorage storage,
                              IStoredSessionManager storedSessionManager,
                              IDateTimeProvider dateTimeProvider)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));

            if (storedSessionManager == null)
                throw new ArgumentNullException(nameof(storedSessionManager));

            if (dateTimeProvider == null)
                throw new ArgumentNullException(nameof(dateTimeProvider));

            _storage = storage;
            _storedSessionManager = storedSessionManager;
            _dateTimeProvider = dateTimeProvider;
        }

        public async Task<bool> TryBeginSessionAsync(string session, DateTime leaseEnd, CancellationToken cancellation = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var newSession = _storedSessionManager.Begin(session, leaseEnd);

            var previousSession = await _storage.UpdateSessionAsync(null, newSession, cancellation);

            return previousSession == null;
        }

        public async Task UpdateSessionAsync(string session, DateTime leaseEnd, CancellationToken cancellation)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            IStoredSession current = await _storage.GetSessionAsync(session, cancellation),
                     start,
                     desired;

            do
            {
                start = current;

                if (start == null || _storedSessionManager.IsEnded(start))
                {
                    throw new SessionTerminatedException(session);
                }

                desired = _storedSessionManager.UpdateLease(start, leaseEnd);

                current = await _storage.UpdateSessionAsync(start, desired, cancellation);
            }
            while (start != current);
        }

        public async Task EndSessionAsync(string session, CancellationToken cancellation)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            IStoredSession current = await _storage.GetSessionAsync(session, cancellation),
                     start,
                     desired;

            do
            {
                start = current;

                if (start == null)
                {
                    return;
                }

                desired = start.Entries.Any() ? _storedSessionManager.End(start) : null;
                current = await _storage.UpdateSessionAsync(start, desired, cancellation);
            }
            while (start != current);
        }

        public Task WaitForTerminationAsync(string session, CancellationToken cancellation)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            lock (_sessionTerminationCache)
            {
                if (_sessionTerminationCache.TryGetValue(session, out var task))
                {
                    if (task.IsCompleted)
                    {
                        _sessionTerminationCache.Remove(session);
                    }

                    return task;
                }
            }

            var internalCancellationSource = new CancellationTokenSource();
            var result = InternalWaitForTerminationAsync(session, internalCancellationSource.Token);

            // The session is already terminated.
            if (result.IsCompleted)
            {
                return result;
            }

            lock (_sessionTerminationCache)
            {
                if (_sessionTerminationCache.ContainsKey(session))
                {
                    internalCancellationSource.Cancel();

                    result = _sessionTerminationCache[session];
                }
                else
                {
                    _sessionTerminationCache.Add(session, result);
                }
            }

            if (cancellation.CanBeCanceled)
            {
                return result.WithCancellation(cancellation);
            }

            return result;
        }

        private async Task InternalWaitForTerminationAsync(string session, CancellationToken cancellation)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var start = await _storage.GetSessionAsync(session, cancellation);

            while (start != null)
            {
                if (_storedSessionManager.IsEnded(start))
                {
                    lock (_sessionTerminationCache)
                    {
                        _sessionTerminationCache.Remove(session);
                    }

                    return;
                }

                var now = _dateTimeProvider.GetCurrentTime();
                var timeToWait = start.LeaseEnd - now;

                await Task.Delay(timeToWait, cancellation);

                var current = await _storage.GetSessionAsync(start.Key, cancellation);

                if (start != current)
                {
                    start = current;
                }
            }

            return;
        }

        public async Task<string> WaitForTerminationAsync(CancellationToken cancellation)
        {
            while (cancellation.ThrowOrContinue())
            {
                var sessions = await _storage.GetSessionsAsync(cancellation);

                var delay = TimeSpan.FromSeconds(2);

                foreach (var session in sessions)
                {
                    if (_storedSessionManager.IsEnded(session))
                        return session.Key;

                    var now = _dateTimeProvider.GetCurrentTime();
                    var timeToWait = session.LeaseEnd - now;

                    if (timeToWait < delay)
                        delay = timeToWait;
                }

                await Task.Delay(delay, cancellation);
            }

            Assert(false);
            return null;
        }

        public async Task<bool> IsAliveAsync(string session, CancellationToken cancellation = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var s = await _storage.GetSessionAsync(session, cancellation);

            return s != null && !_storedSessionManager.IsEnded(s);
        }

        public async Task AddSessionEntryAsync(string session, string entry, CancellationToken cancellation = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            IStoredSession current = await _storage.GetSessionAsync(session, cancellation),
                           start,
                           desired;

            do
            {
                start = current;

                if (start == null || _storedSessionManager.IsEnded(start))
                {
                    throw new SessionTerminatedException();
                }

                desired = _storedSessionManager.AddEntry(start, entry);

                current = await _storage.UpdateSessionAsync(start, desired, cancellation);
            }
            while (start != current);
        }

        public async Task RemoveSessionEntryAsync(string session, string entry, CancellationToken cancellation = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            IStoredSession current = await _storage.GetSessionAsync(session, cancellation),
                     start,
                     desired;

            do
            {
                start = current;

                if (start == null)
                {
                    return;
                }

                desired = _storedSessionManager.RemoveEntry(start, entry);

                if (_storedSessionManager.IsEnded(desired) && !desired.Entries.Any())
                {
                    desired = null;
                }

                current = await _storage.UpdateSessionAsync(start, desired, cancellation);
            }
            while (start != current);
        }

        public async Task<IEnumerable<string>> GetEntriesAsync(string session, CancellationToken cancellation = default)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            var current = await _storage.GetSessionAsync(session, cancellation);

            if (current == null)
                return Enumerable.Empty<string>();

            return current.Entries;
        }

        public async Task<IEnumerable<string>> GetSessionsAsync(CancellationToken cancellation = default)
        {
            return (await _storage.GetSessionsAsync(cancellation)).Where(p => !_storedSessionManager.IsEnded(p)).Select(p => p.Key);
        }
    }
}