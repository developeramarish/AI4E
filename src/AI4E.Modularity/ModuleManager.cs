/* License
 * --------------------------------------------------------------------------------------------------------------------
 * This file is part of the AI4E distribution.
 *   (https://github.com/AI4E/AI4E)
 * Copyright (c) 2018 Andreas Truetschel and contributors.
 * 
 * AI4E is free software: you can redistribute it and/or modify  
 * it under the terms of the GNU Lesser General Public License as   
 * published by the Free Software Foundation, version 3.
 *
 * AI4E is distributed in the hope that it will be useful, but 
 * WITHOUT ANY WARRANTY; without even the implied warranty of 
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU 
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <http://www.gnu.org/licenses/>.
 * --------------------------------------------------------------------------------------------------------------------
 */

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Coordination;
using AI4E.Internal;
using AI4E.Routing;
using AI4E.Utils.Memory;
using AI4E.Utils.Memory.Compatibility;
using static System.Diagnostics.Debug;

namespace AI4E.Modularity
{
    public sealed class ModuleManager : IModuleManager
    {
        private static readonly byte[] _emptyPayload = new byte[0];
        private const string _whitespaceRegexPattern = @"\s+";
        private static readonly Regex _whitespaceRegex = new Regex(_whitespaceRegexPattern, RegexOptions.CultureInvariant |
                                                                                            RegexOptions.Singleline |
                                                                                            RegexOptions.IgnoreCase |
                                                                                            RegexOptions.Compiled);

        private static readonly CoordinationEntryPath _rootPath = new CoordinationEntryPath("modules");
        private static readonly CoordinationEntryPath _rootPrefixesPath = _rootPath.GetChildPath("prefixes"); // prefix => end-point
        private static readonly CoordinationEntryPath _rootRunningPath = _rootPath.GetChildPath("running"); // module => (prefixes, end-point)

        private readonly ICoordinationManager _coordinationManager;

        #region C'tor

        public ModuleManager(ICoordinationManager coordinationManager)
        {
            if (coordinationManager == null)
                throw new ArgumentNullException(nameof(coordinationManager));

            _coordinationManager = coordinationManager;
        }

        #endregion

        #region IRunningModuleLookup

        public async Task AddModuleAsync(ModuleIdentifier module, EndPointAddress endPoint, IEnumerable<ReadOnlyMemory<char>> prefixes, CancellationToken cancellation)
        {
            if (module == default)
                throw new ArgumentDefaultException(nameof(module));

            if (endPoint == default)
                throw new ArgumentDefaultException(nameof(endPoint));

            if (prefixes == null)
                throw new ArgumentNullException(nameof(prefixes));

            if (!prefixes.Any())
                throw new ArgumentException("The collection must not be empty.", nameof(prefixes));

            if (prefixes.Any(prefix => prefix.Span.IsEmptyOrWhiteSpace()))
                throw new ArgumentException("The collection must not contain null entries or entries that are empty or contain whitespace only.", nameof(prefixes));

            var prefixCollection = (prefixes as ICollection<ReadOnlyMemory<char>>) ?? prefixes.ToList();
            var session = await _coordinationManager.GetSessionAsync(cancellation);

            var tasks = new List<Task>(capacity: prefixCollection.Count());

            foreach (var prefix in prefixCollection)
            {
                tasks.Add(WriteModulePrefixEntryAsync(prefix, endPoint, session, cancellation));
            }

            await Task.WhenAll(tasks);

            await WriteRunningModuleEntryAsync(module, endPoint, prefixCollection, session, cancellation);

            // TODO: When cancelled, alls completed operations should be reverted.
            // TODO: The RemoveModuleAsync alogrithm assumes that there are no prefix entries, if the running module entry is not present. We should reflect this assumtion here.
        }

        public async Task RemoveModuleAsync(ModuleIdentifier module, CancellationToken cancellation)
        {
            if (module == default)
                throw new ArgumentDefaultException(nameof(module));

            var session = await _coordinationManager.GetSessionAsync(cancellation);
            var runningModulePath = GetRunningModulePath(module, session);

            var entry = await _coordinationManager.GetAsync(runningModulePath, cancellation);

            if (entry == null)
                return;

            await _coordinationManager.DeleteAsync(runningModulePath, cancellation: cancellation);

            var (endPoint, prefixes) = ReadRunningModuleEntry(entry);

            foreach (var prefix in prefixes)
            {
                var prefixPath = GetPrefixPath(prefix, endPoint, session, normalize: false);
                await _coordinationManager.DeleteAsync(prefixPath, cancellation: cancellation);
            }
        }

        public async ValueTask<IEnumerable<EndPointAddress>> GetEndPointsAsync(ReadOnlyMemory<char> prefix, CancellationToken cancellation)
        {
            if (prefix.Span.IsEmptyOrWhiteSpace())
                throw new ArgumentException("The argument must not be empty, not consist of whitespace only.", nameof(prefix));

            var normalizedPrefix = NormalizePrefix(prefix);

            // It is not possible to register an end-point address for the root path.
            if (normalizedPrefix.IsEmpty || normalizedPrefix.Span[0] == '_')
            {
                return Enumerable.Empty<EndPointAddress>();
            }

            var path = GetPrefixPath(normalizedPrefix, normalize: false);
            var entry = await _coordinationManager.GetOrCreateAsync(path, _emptyPayload, EntryCreationModes.Default, cancellation);

            Assert(entry != null);

            var result = new List<EndPointAddress>(capacity: entry.Children.Count);
            var childEntries = (await entry.GetChildrenEntriesAsync(cancellation)).OrderBy(p => p.CreationTime).ToList();

            foreach (var childEntry in childEntries)
            {
                var endPoint = ReadModulePrefixEntry(childEntry);
                result.Add(endPoint);
            }

            return result;
        }

        public async ValueTask<IEnumerable<ReadOnlyMemory<char>>> GetPrefixesAsync(ModuleIdentifier module, CancellationToken cancellation)
        {
            if (module == default)
                throw new ArgumentDefaultException(nameof(module));

            var runningModulePath = GetRunningModulePath(module);

            var entry = await _coordinationManager.GetAsync(runningModulePath, cancellation);

            if (entry == null)
                return Enumerable.Empty<ReadOnlyMemory<char>>();

            return await entry.GetChildrenEntries().SelectMany(p => ReadRunningModuleEntry(p).prefixes.ToAsyncEnumerable()).Distinct().ToList();
        }

        public async ValueTask<IEnumerable<EndPointAddress>> GetEndPointsAsync(ModuleIdentifier module, CancellationToken cancellation)
        {
            if (module == default)
                throw new ArgumentDefaultException(nameof(module));

            var runningModulePath = GetRunningModulePath(module);

            var entry = await _coordinationManager.GetAsync(runningModulePath, cancellation);

            if (entry == null)
                return Enumerable.Empty<EndPointAddress>();

            return await entry.GetChildrenEntries().Select(p => ReadRunningModuleEntry(p).endPoint).Distinct().ToList();
        }

        #endregion

        private EndPointAddress ReadModulePrefixEntry(IEntry entry)
        {
            var reader = new BinarySpanReader(entry.Value.Span, ByteOrder.LittleEndian);
            return ReadEndPointAddress(ref reader);
        }

        private (EndPointAddress endPoint, IReadOnlyCollection<ReadOnlyMemory<char>> prefixes) ReadRunningModuleEntry(IEntry entry)
        {
            var reader = new BinarySpanReader(entry.Value.Span, ByteOrder.LittleEndian);
            var endPoint = ReadEndPointAddress(ref reader);
            var prefixesCount = reader.ReadInt32();

            var prefixes = new List<ReadOnlyMemory<char>>(capacity: prefixesCount);

            for (var i = 0; i < prefixesCount; i++)
            {
                var prefix = reader.ReadString().AsMemory();
                prefixes.Add(prefix);
            }

            return (endPoint, prefixes);
        }

        private async Task WriteRunningModuleEntryAsync(
            ModuleIdentifier module,
            EndPointAddress endPoint,
            ICollection<ReadOnlyMemory<char>> prefixes,
            Session session,
            CancellationToken cancellation)
        {
            var path = GetRunningModulePath(module, session);

            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(endPoint);
                    writer.Write(prefixes.Count());

                    foreach (var prefix in prefixes)
                    {
                        WritePrefix(writer, prefix);
                    }
                }

                var payload = stream.ToArray();
                await _coordinationManager.GetOrCreateAsync(path, payload, EntryCreationModes.Ephemeral, cancellation);
            }
        }

        private async Task WriteModulePrefixEntryAsync(ReadOnlyMemory<char> prefix, EndPointAddress endPoint, Session session, CancellationToken cancellation)
        {
            var normalizedPrefix = NormalizePrefix(prefix);

            if (normalizedPrefix.Span[0] == '_')
                throw new ArgumentException("A prefix must not begin with an underscore.");

            var path = GetPrefixPath(normalizedPrefix, endPoint, session, normalize: false);

            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(endPoint);
                }

                var payload = stream.ToArray();
                var entry = await _coordinationManager.GetOrCreateAsync(path, payload, EntryCreationModes.Ephemeral, cancellation);
            }
        }

        private EndPointAddress ReadEndPointAddress(ref BinarySpanReader reader)
        {
            var localEndPointBytesLenght = reader.ReadInt32();

            if (localEndPointBytesLenght == 0)
            {
                return EndPointAddress.UnknownAddress;
            }

            var utf8EncodedValue = reader.Read(localEndPointBytesLenght);
            var copy = utf8EncodedValue.ToArray(); // TODO

            return new EndPointAddress(copy);
        }

        private void WritePrefix(BinaryWriter writer, ReadOnlyMemory<char> prefix)
        {
            var normalizedPrefix = NormalizePrefix(prefix);

            using (ArrayPool<byte>.Shared.RentExact(Encoding.UTF8.GetByteCount(prefix.Span), out var memory))
            {
                var byteCount = Encoding.UTF8.GetBytes(prefix.Span, memory.Span);
                Assert(byteCount == memory.Length);

                writer.Write(byteCount);
                writer.Write(memory.Span);
            }
        }

        private static ReadOnlyMemory<char> NormalizePrefix(ReadOnlyMemory<char> prefix)
        {
            prefix = _whitespaceRegex.Replace(prefix.ToString(), "").AsMemory(); // TODO: This does take a copy

            if (prefix.Span.StartsWith("/".AsSpan()))
            {
                prefix = prefix.Slice(1);
            }

            return prefix;
        }

        private static CoordinationEntryPath GetPrefixPath(ReadOnlyMemory<char> prefix, bool normalize = true)
        {
            if (normalize)
            {
                prefix = NormalizePrefix(prefix);
            }

            return _rootPrefixesPath.GetChildPath(prefix);
        }

        private static CoordinationEntryPath GetPrefixPath(ReadOnlyMemory<char> prefix, EndPointAddress endPoint, Session session, bool normalize = true)
        {
            if (normalize)
            {
                prefix = NormalizePrefix(prefix);
            }

            var uniqueEntryName = IdGenerator.GenerateId(endPoint.ToString(), session.ToString());
            return _rootPrefixesPath.GetChildPath(prefix, uniqueEntryName.AsMemory());
        }

        private static CoordinationEntryPath GetRunningModulePath(ModuleIdentifier module)
        {
            return _rootRunningPath.GetChildPath(module.Name);
        }

        private static CoordinationEntryPath GetRunningModulePath(ModuleIdentifier module, Session session)
        {
            return _rootRunningPath.GetChildPath(module.Name, session.ToString());
        }
    }
}
