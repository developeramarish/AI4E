using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Internal;
using AI4E.Utils;
using Microsoft.Extensions.Logging;
using static System.Diagnostics.Debug;

namespace AI4E.Storage.Transactions
{
    public sealed class TransactionalDatabase : IDatabase
    {
        private readonly ITransactionManager _transactionManager;
        private readonly ILoggerFactory _loggerFactory;

        public TransactionalDatabase(ITransactionManager transactionManager,
                                     ILoggerFactory loggerFactory = null)
        {
            if (transactionManager == null)
                throw new ArgumentNullException(nameof(transactionManager));

            _transactionManager = transactionManager;
            _loggerFactory = loggerFactory;
        }

        public IScopedTransactionalDatabase CreateScope()
        {
            var logger = _loggerFactory?.CreateLogger<ScopedTransactionalDatabase>();

            return new ScopedTransactionalDatabase(_transactionManager,
                                                   logger);
        }

        public ValueTask<long> GetUniqueResourceIdAsync(string resourceKey, CancellationToken cancellation)
        {
            throw new NotImplementedException();  // TODO: We have to optimize this by calling to the underlying database.
        }

        public async Task<bool> AddAsync<TEntry>(TEntry entry, CancellationToken cancellation)
            where TEntry : class
        {
            var predicate = DataPropertyHelper.BuildPredicate(entry);

            using (var scopedDatabase = CreateScope())
            {
                do
                {
                    var matches = scopedDatabase.GetAsync(predicate, cancellation);

                    if (await matches.Any(cancellation))
                    {
                        return false;
                    }

                    await scopedDatabase.StoreAsync(entry, cancellation);
                }
                while (await scopedDatabase.TryCommitAsync(cancellation));
            }

            return true;
        }

        // TODO: This is copied from MongoDatabase.cs
        private static Expression<Func<TEntry, bool>> BuildPredicate<TEntry>(TEntry comparand,
                                                                             Expression<Func<TEntry, bool>> predicate)
        {
            Assert(comparand != null);
            Assert(predicate != null);

            var parameter = predicate.Parameters.First();
            var idSelector = DataPropertyHelper.BuildPredicate(comparand);

            var body = Expression.AndAlso(ParameterExpressionReplacer.ReplaceParameter(idSelector.Body, idSelector.Parameters.First(), parameter),
                                          predicate.Body);

            return Expression.Lambda<Func<TEntry, bool>>(body, parameter);
        }

        public async Task<bool> UpdateAsync<TEntry>(TEntry entry, Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation = default)
            where TEntry : class
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            var combinedPredicate = BuildPredicate(entry, predicate);

            using (var scopedDatabase = CreateScope())
            {
                do
                {
                    var matches = scopedDatabase.GetAsync(combinedPredicate, cancellation);

                    if (!await matches.Any(cancellation))
                    {
                        return false;
                    }

                    await scopedDatabase.StoreAsync(entry, cancellation);
                }
                while (await scopedDatabase.TryCommitAsync(cancellation));
            }

            return true;
        }

        public async Task<bool> RemoveAsync<TEntry>(TEntry entry, Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation = default)
            where TEntry : class
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            var combinedPredicate = BuildPredicate(entry, predicate);

            using (var scopedDatabase = CreateScope())
            {
                do
                {
                    var matches = scopedDatabase.GetAsync(combinedPredicate, cancellation);

                    if (!await matches.Any(cancellation))
                    {
                        return false;
                    }

                    await scopedDatabase.RemoveAsync(entry, cancellation);
                }
                while (await scopedDatabase.TryCommitAsync(cancellation));
            }

            return true;
        }

        public Task Clear<TEntry>(CancellationToken cancellation = default)
            where TEntry : class
        {
            throw new NotImplementedException(); // TODO: We have to optimize this by calling to the underlying database.
        }

        public async ValueTask<TEntry> GetOrAdd<TEntry>(TEntry entry, CancellationToken cancellation = default)
            where TEntry : class
        {
            TEntry result;

            var predicate = DataPropertyHelper.BuildPredicate(entry);

            using (var scopedDatabase = CreateScope())
            {
                do
                {
                    result = entry;

                    var matches = scopedDatabase.GetAsync(predicate, cancellation);
                    var enumerator = matches.GetEnumerator();

                    try
                    {
                        if (await enumerator.MoveNext(cancellation))
                        {
                            result = enumerator.Current;
                        }
                    }
                    finally
                    {
                        enumerator.Dispose();
                    }

                    await scopedDatabase.StoreAsync(entry, cancellation);
                }
                while (await scopedDatabase.TryCommitAsync(cancellation));
            }

            return result;
        }

        public IAsyncEnumerable<TEntry> GetAsync<TEntry>(CancellationToken cancellation = default)
            where TEntry : class
        {
            return GetAsync<TEntry>(_ => true, cancellation);
        }

        public IAsyncEnumerable<TEntry> GetAsync<TEntry>(Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation = default)
            where TEntry : class
        {
            return new TransactionDatabaseAsyncEnumerable<TEntry>(this, predicate, cancellation);
        }

        public ValueTask<TEntry> GetOneAsync<TEntry>(Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation = default)
            where TEntry : class
        {
            return new ValueTask<TEntry>(GetAsync(predicate, cancellation).FirstOrDefault(cancellation));
        }

        bool IDatabase.SupportsScopes => true;

        private sealed class TransactionDatabaseAsyncEnumerable<TEntry> : IAsyncEnumerable<TEntry>
            where TEntry : class
        {
            private readonly TransactionalDatabase _database;
            private readonly Expression<Func<TEntry, bool>> _predicate;
            private readonly CancellationToken _cancellation;

            public TransactionDatabaseAsyncEnumerable(TransactionalDatabase database, Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation)
            {
                _database = database;
                _predicate = predicate;
                _cancellation = cancellation;
            }

            public IAsyncEnumerator<TEntry> GetEnumerator()
            {
                var scopedDatabase = _database.CreateScope();

                try
                {
                    var enumerable = scopedDatabase.GetAsync(_predicate, _cancellation);
                    return new TransactionalDatabaseAsyncEnumerator<TEntry>(scopedDatabase, enumerable.GetEnumerator());
                }
                catch
                {
                    scopedDatabase.Dispose();
                    throw;
                }
            }
        }

        private sealed class TransactionalDatabaseAsyncEnumerator<TEntry> : IAsyncEnumerator<TEntry>
            where TEntry : class
        {
            private readonly IScopedTransactionalDatabase _scopedDatabase;
            private readonly IAsyncEnumerator<TEntry> _enumerator;

            public TransactionalDatabaseAsyncEnumerator(IScopedTransactionalDatabase scopedDatabase, IAsyncEnumerator<TEntry> enumerator)
            {
                _scopedDatabase = scopedDatabase;
                _enumerator = enumerator;
            }

            public Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                return _enumerator.MoveNext(cancellationToken);
            }

            public TEntry Current => _enumerator.Current;

            public void Dispose()
            {
                try
                {
                    _enumerator.Dispose();
                }
                finally
                {
                    _scopedDatabase.Dispose();
                }
            }
        }
    }
}
