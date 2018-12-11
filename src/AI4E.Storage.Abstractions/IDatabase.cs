/* Summary
 * --------------------------------------------------------------------------------------------------------------------
 * Filename:        IDatabase.cs 
 * Types:           (1) AI4E.Storage.IDatabase
 *                  (2) AI4E.Storage.IFilterableDatabase
 *                  (3) AI4E.Storage.IQueryableDatabase
 * Version:         1.0
 * Author:          Andreas Trütschel
 * Last modified:   04.06.2018 
 * --------------------------------------------------------------------------------------------------------------------
 */

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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Internal;
using AI4E.Storage.Transactions;
using AI4E.Utils;
using static System.Diagnostics.Debug;

namespace AI4E.Storage
{
    /// <summary>
    /// An abstraction of a database with minimal functionality.
    /// </summary>
    public interface IDatabase
    {
        ValueTask<long> GetUniqueResourceIdAsync(string resourceKey, CancellationToken cancellation);

        /// <summary>
        /// Asynchronously tries to add the specified entry into the database.
        /// </summary>
        /// <typeparam name="TEntry">The type of entry.</typeparam>
        /// <param name="entry">The entry that shall be added.</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// A value task that represents the asynchronous operation.
        /// When evaluated, the tasks result contains a boolean value indicating whether the entry was added successfully.
        /// </returns>
        /// <remarks>
        /// The entry is added successfully, if the database does not contain an entry with the same id than the specified entry.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="entry"/> is null.</exception>
        /// <exception cref="StorageException">Thrown if an unresolvable exception occurs in the storage subsystem.</exception>
        /// <exception cref="StorageUnavailableException">Thrown if the storage subsystem is unavailable or unreachable.</exception>
        Task<bool> AddAsync<TEntry>(TEntry entry, CancellationToken cancellation = default)
            where TEntry : class;

        /// <summary>
        /// Asynchronously tries to update the specified entry in the database.
        /// </summary>
        /// <typeparam name="TEntry">The type of entry.</typeparam>
        /// <param name="entry">The entry that shall be updated in the database.</param>
        /// <param name="predicate">A predicate that the current database entry must match in order to perform the update operation.</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// A value task that represents the asynchronous operation.
        /// When evaluated, the tasks result contains a boolean value indicating whether the entry was updated successfully.
        /// </returns>
        /// <remarks>
        /// The entry is updated successfully, if the database does contain an entry with the same id than the specified entry and it matched the predicate.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if either <paramref name="entry"/> or <paramref name="predicate"/> is null.</exception>
        /// <exception cref="StorageException">Thrown if an unresolvable exception occurs in the storage subsystem.</exception>
        /// <exception cref="StorageUnavailableException">Thrown if the storage subsystem is unavailable or unreachable.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the database does not support the specified predicate.</exception>
        Task<bool> UpdateAsync<TEntry>(TEntry entry, Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation = default)
            where TEntry : class;

        /// <summary>
        /// Asynchronously tries to remove the specified entry from the database.
        /// </summary>
        /// <typeparam name="TEntry">The type of entry.</typeparam>
        /// <param name="entry">The entry that shall be removed from the database.</param>
        /// <param name="predicate">A predicate that the current database entry must match in order to perform the update operation.</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// A value task that represents the asynchronous operation.
        /// When evaluated, the tasks result contains a boolean value indicating whether the entry was removed successfully.
        /// </returns>
        /// <remarks>
        /// The entry is removed successfully, if the database does contain an entry with the same id than the specified entry and it matched the predicate.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if either <paramref name="entry"/> or <paramref name="predicate"/> is null.</exception>
        /// <exception cref="StorageException">Thrown if an unresolvable exception occurs in the storage subsystem.</exception>
        /// <exception cref="StorageUnavailableException">Thrown if the storage subsystem is unavailable or unreachable.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the database does not support the specified predicate.</exception>
        Task<bool> RemoveAsync<TEntry>(TEntry entry, Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation = default)
            where TEntry : class;

        Task Clear<TEntry>(CancellationToken cancellation = default)
            where TEntry : class;

        ValueTask<TEntry> GetOrAdd<TEntry>(TEntry entry, CancellationToken cancellation = default)
            where TEntry : class;

        /// <summary>
        /// Asynchronously retrieves a collection of all stored entries.
        /// </summary>
        /// <typeparam name="TEntry">The type of entry.</typeparam>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// An async enumerable that enumerates all stored entries of type <typeparamref name="TEntry"/>.
        /// </returns>
        /// <exception cref="StorageException">Thrown if an unresolvable exception occurs in the storage subsystem.</exception>
        /// <exception cref="StorageUnavailableException">Thrown if the storage subsystem is unavailable or unreachable.</exception>
        IAsyncEnumerable<TEntry> GetAsync<TEntry>(CancellationToken cancellation = default)
            where TEntry : class;

        /// <summary>
        /// Asynchronously retrieves a collection of all stored entries that match the specified predicate.
        /// </summary>
        /// <typeparam name="TEntry">The type of entry.</typeparam>
        /// <param name="predicate">The predicate that the entries must match.</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// An async enumerable that enumerates all stored entries of type <typeparamref name="TEntry"/> that match the specified predicate.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="predicate"/> is null.</exception>
        /// <exception cref="StorageException">Thrown if an unresolvable exception occurs in the storage subsystem.</exception>
        /// <exception cref="StorageUnavailableException">Thrown if the storage subsystem is unavailable or unreachable.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the database does not support the specified predicate.</exception>
        IAsyncEnumerable<TEntry> GetAsync<TEntry>(Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation = default)
            where TEntry : class;

        ValueTask<TEntry> GetOneAsync<TEntry>(Expression<Func<TEntry, bool>> predicate, CancellationToken cancellation = default)
            where TEntry : class;

        IScopedTransactionalDatabase CreateScope();

        bool SupportsScopes { get; }
    }

    /// <summary>
    /// An abstraction of a database with queryable functionality.
    /// </summary>
    public interface IQueryableDatabase : IDatabase
    {
        /// <summary>
        /// Asynchronously performs a database query specified by a query shaper.
        /// </summary>
        /// <typeparam name="TEntry">The type of entry.</typeparam>
        /// <typeparam name="TResult">The type of result.</typeparam>
        /// <param name="queryShaper">A function that specifies the database query.</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// An async enumerable that enumerates items of type <typeparamref name="TResult"/> that are the query result.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="queryShaper"/> is null.</exception>
        /// <exception cref="StorageException">Thrown if an unresolvable exception occurs in the storage subsystem.</exception>
        /// <exception cref="StorageUnavailableException">Thrown if the storage subsystem is unavailable or unreachable.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the database does not support the specified query.</exception>
        IAsyncEnumerable<TResult> QueryAsync<TEntry, TResult>(Func<IQueryable<TEntry>, IQueryable<TResult>> queryShaper, CancellationToken cancellation = default)
            where TEntry : class;
    }

    public static class DatabaseExtension
    {
        private static readonly MethodInfo _addMethodDefinition;
        private static readonly MethodInfo _updateMethodDefinition;
        private static readonly MethodInfo _removeMethodDefinition;

        static DatabaseExtension()
        {
            _addMethodDefinition = typeof(DatabaseExtension).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                                                               .Single(p => p.Name == nameof(DatabaseExtension.AddAsync) &&
                                                                            p.IsGenericMethodDefinition);
            _updateMethodDefinition = typeof(DatabaseExtension).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                                                               .Single(p => p.Name == nameof(DatabaseExtension.UpdateAsync) &&
                                                                            p.IsGenericMethodDefinition);
            _removeMethodDefinition = typeof(DatabaseExtension).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                                                               .Single(p => p.Name == nameof(DatabaseExtension.RemoveAsync) &&
                                                                            p.IsGenericMethodDefinition);
        }

        private static readonly ConcurrentDictionary<Type, Func<IDatabase, object, CancellationToken, Task<bool>>> _addMethods = new ConcurrentDictionary<Type, Func<IDatabase, object, CancellationToken, Task<bool>>>();
        private static readonly ConcurrentDictionary<Type, Func<IDatabase, object, CancellationToken, Task<bool>>> _updateMethods = new ConcurrentDictionary<Type, Func<IDatabase, object, CancellationToken, Task<bool>>>();
        private static readonly ConcurrentDictionary<Type, Func<IDatabase, object, CancellationToken, Task<bool>>> _removeMethods = new ConcurrentDictionary<Type, Func<IDatabase, object, CancellationToken, Task<bool>>>();

        private static readonly Func<Type, Func<IDatabase, object, CancellationToken, Task<bool>>> _buildAddMethodCache = BuildAddMethod;
        private static readonly Func<Type, Func<IDatabase, object, CancellationToken, Task<bool>>> _buildUpdateMethodCache = BuildUpdateMethod;
        private static readonly Func<Type, Func<IDatabase, object, CancellationToken, Task<bool>>> _buildRemoveMethodCache = BuildRemoveMethod;

        private static Func<IDatabase, object, CancellationToken, Task<bool>> GetAddMethod(Type dataType)
        {
            return _addMethods.GetOrAdd(dataType, _buildAddMethodCache);
        }

        private static Func<IDatabase, object, CancellationToken, Task<bool>> GetUpdateMethod(Type dataType)
        {
            return _updateMethods.GetOrAdd(dataType, _buildUpdateMethodCache);
        }

        private static Func<IDatabase, object, CancellationToken, Task<bool>> GetRemoveMethod(Type dataType)
        {
            return _removeMethods.GetOrAdd(dataType, _buildRemoveMethodCache);
        }

        private static Func<IDatabase, object, CancellationToken, Task<bool>> BuildMethod(MethodInfo methodDefinition, Type dataType)
        {
            Assert(methodDefinition.IsGenericMethodDefinition);
            Assert(methodDefinition.GetGenericArguments().Length == 1);

            var method = methodDefinition.MakeGenericMethod(dataType);

            Assert(method.ReturnType == typeof(Task<bool>));
            Assert(method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new Type[] { typeof(IDatabase), dataType, typeof(CancellationToken) }));

            var databaseParameter = Expression.Parameter(typeof(IDatabase), "database");
            var entryParameter = Expression.Parameter(typeof(object), "entry");
            var cancellationParameter = Expression.Parameter(typeof(CancellationToken), "cancellation");
            var convertedEntry = Expression.Convert(entryParameter, dataType);
            var call = Expression.Call(method, databaseParameter, convertedEntry, cancellationParameter);
            return Expression.Lambda<Func<IDatabase, object, CancellationToken, Task<bool>>>(call, databaseParameter, entryParameter, cancellationParameter).Compile();
        }

        private static Func<IDatabase, object, CancellationToken, Task<bool>> BuildAddMethod(Type dataType)
        {
            return BuildMethod(_addMethodDefinition, dataType);
        }

        private static Func<IDatabase, object, CancellationToken, Task<bool>> BuildUpdateMethod(Type dataType)
        {
            return BuildMethod(_updateMethodDefinition, dataType);
        }

        private static Func<IDatabase, object, CancellationToken, Task<bool>> BuildRemoveMethod(Type dataType)
        {
            return BuildMethod(_removeMethodDefinition, dataType);
        }

        private static Task<bool> AddAsync<TEntry>(IDatabase database, TEntry entry, CancellationToken cancellation)
            where TEntry : class
        {
            return database.AddAsync(entry, cancellation);
        }

        private static Task<bool> UpdateAsync<TEntry>(IDatabase database, TEntry entry, CancellationToken cancellation)
            where TEntry : class
        {
            return database.UpdateAsync(entry, _ => true, cancellation);
        }

        private static Task<bool> RemoveAsync<TEntry>(IDatabase database, TEntry entry, CancellationToken cancellation)
           where TEntry : class
        {
            return database.RemoveAsync(entry, _ => true, cancellation);
        }

        /// <summary>
        /// Stores an object in the store.
        /// </summary>
        /// <param name="database">The data store.</param>
        /// <param name="data">The object to update.</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown if either <paramref name="database"/> or <paramref name="data"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the object is disposed.</exception>
        public static Task<bool> AddAsync(this IDatabase database, object data, CancellationToken cancellation = default)
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data is ValueType)
                throw new ArgumentException("The argument must be a reference type.", nameof(data));

            return database.AddAsync(data.GetType(), data, cancellation);
        }

        public static Task<bool> UpdateAsync(this IDatabase database, object data, CancellationToken cancellation = default)
        {
            return database.UpdateAsync(data.GetType(), data, cancellation);
        }

        /// <summary>
        /// Removes an object from the store.
        /// </summary>
        /// <param name="database">The data store.</param>
        /// <param name="data">The object to remove.</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown if either <paramref name="database"/> or <paramref name="data"/> is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown if the object is disposed.</exception>
        public static Task<bool> RemoveAsync(this IDatabase database, object data, CancellationToken cancellation = default)
        {
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data is ValueType)
                throw new ArgumentException("The argument must be a reference type.", nameof(data));

            return database.RemoveAsync(data.GetType(), data, cancellation);
        }

        public static Task<bool> AddAsync(this IDatabase database, Type dataType, object data, CancellationToken cancellation = default)
        {
            CheckArguments(database, dataType, data);
            var invoker = GetAddMethod(dataType);
            return invoker(database, data, cancellation);
        }

        public static Task<bool> UpdateAsync(this IDatabase database, Type dataType, object data, CancellationToken cancellation = default)
        {
            CheckArguments(database, dataType, data);
            var invoker = GetUpdateMethod(dataType);
            return invoker(database, data, cancellation);
        }

        public static Task<bool> RemoveAsync(this IDatabase database, Type dataType, object data, CancellationToken cancellation = default)
        {
            CheckArguments(database, dataType, data);
            var invoker = GetRemoveMethod(dataType);
            return invoker(database, data, cancellation);
        }

        private static void CheckArguments(IDatabase dataStore, Type dataType, object data)
        {
            if (dataStore == null)
                throw new ArgumentNullException(nameof(dataStore));

            if (dataType == null)
                throw new ArgumentNullException(nameof(dataType));

            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (dataType.IsValueType)
                throw new ArgumentException("The argument must be a reference type.", nameof(dataType));

            if (!dataType.IsAssignableFrom(data.GetType()))
                throw new ArgumentException($"The specified data must be of type '{dataType.FullName}' or an assignable type.");
        }

        /// <summary>
        /// Asynchronously replaces an entry with the specified one if the existing entry equals the specified comparand.
        /// </summary>
        /// <typeparam name="TEntry">The type of entry.</typeparam>
        /// <param name="entry">The entry to insert on succcess.</param>
        /// <param name="comparand">The comparand entry.</param>
        /// <param name="equalityComparer">An expression that specifies the equality of two entries,</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// A value task that represents the asynchronous operation.
        /// When evaluated, the tasks result contains a boolean value indicating whether the entry was inserted successfully.
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="equalityComparer"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// Throw if either both, <paramref name="entry"/> and <paramref name="comparand"/> are null or 
        ///       if both are non null and the id of <paramref name="entry"/> does not match the id of <paramref name="comparand"/>. 
        /// </exception>
        /// <exception cref="StorageException">Thrown if an unresolvable exception occurs in the storage subsystem.</exception>
        /// <exception cref="StorageUnavailableException">Thrown if the storage subsystem is unavailable or unreachable.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the database does not support the specified equality comparer.</exception>
        public static Task<bool> CompareExchangeAsync<TEntry>(
            this IDatabase database,
            TEntry entry,
            TEntry comparand,
            Expression<Func<TEntry, TEntry, bool>> equalityComparer,
            CancellationToken cancellation = default)
           where TEntry : class
        {
            if (equalityComparer == null)
                throw new ArgumentNullException(nameof(equalityComparer));

            // This is a nop actually. But we check whether comparand is up to date.
            if (entry == comparand)
            {
                return CheckComparandToBeUpToDate(database, comparand, equalityComparer, cancellation);
            }

            // Trying to update an entry.
            if (entry != null && comparand != null)
            {
                return database.UpdateAsync(entry, BuildPredicate(comparand, equalityComparer), cancellation);
            }

            // Trying to create an entry.
            if (entry != null)
            {
                return database.AddAsync(entry, cancellation);
            }

            // Trying to remove an entry.
            Debug.Assert(comparand != null);

            return database.RemoveAsync(comparand, BuildPredicate(comparand, equalityComparer), cancellation);
        }

        private static async Task<bool> CheckComparandToBeUpToDate<TEntry>(
            IDatabase database,
            TEntry comparand,
            Expression<Func<TEntry, TEntry, bool>> equalityComparer,
            CancellationToken cancellation)
            where TEntry : class
        {
            var predicate = DataPropertyHelper.BuildPredicate(comparand);
            var result = await database.GetOneAsync(predicate, cancellation);

            if (comparand == null)
            {
                return result == null;
            }

            if (result == null)
                return false;

            return equalityComparer.Compile(preferInterpretation: true).Invoke(comparand, result);
        }

        private static Expression<Func<TEntry, bool>> BuildPredicate<TEntry>(
            TEntry comparand,
            Expression<Func<TEntry, TEntry, bool>> equalityComparer)
        {
            Debug.Assert(comparand != null);
            Debug.Assert(equalityComparer != null);

            var idSelector = DataPropertyHelper.BuildPredicate(comparand);
            var comparandConstant = Expression.Constant(comparand, typeof(TEntry));
            var parameter = equalityComparer.Parameters.First();
            var equality = ParameterExpressionReplacer.ReplaceParameter(equalityComparer.Body, equalityComparer.Parameters.Last(), comparandConstant);
            var idEquality = ParameterExpressionReplacer.ReplaceParameter(idSelector.Body, idSelector.Parameters.First(), parameter);
            var body = Expression.AndAlso(idEquality, equality);

            return Expression.Lambda<Func<TEntry, bool>>(body, parameter);
        }
    }
}
