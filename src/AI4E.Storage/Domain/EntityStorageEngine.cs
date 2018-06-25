﻿/* Summary
 * --------------------------------------------------------------------------------------------------------------------
 * Filename:        EntityStorageEngine.cs 
 * Types:           (1) AI4E.Storage.Domain.EntityStorageEngine
 * Version:         1.0
 * Author:          Andreas Trütschel
 * Last modified:   23.06.2018 
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Internal;
using JsonDiffPatchDotNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static System.Diagnostics.Debug;
using static AI4E.Internal.DebugEx;

namespace AI4E.Storage.Domain
{
    public sealed partial class EntityStorageEngine : IEntityStorageEngine
    {
        #region Fields

        private readonly IStreamStore _streamStore;
        private readonly IEntityStoragePropertyManager _entityAccessor; // TODO: Rename
        private readonly JsonDiffPatch _differ;
        private readonly JsonSerializer _jsonSerializer;
        private readonly Dictionary<(string bucket, string id, long requestedRevision), (object entity, long revision)> _lookup;
        private bool _isDisposed;

        #endregion

        #region C'tor

        public EntityStorageEngine(IStreamStore streamStore,
                                   IEntityStoragePropertyManager entityAccessor, // TODO: Rename
                                   ISerializerSettingsResolver serializerSettingsResolver)
        {
            if (streamStore == null)
                throw new ArgumentNullException(nameof(streamStore));

            if (entityAccessor == null)
                throw new ArgumentNullException(nameof(entityAccessor));

            if (serializerSettingsResolver == null)
                throw new ArgumentNullException(nameof(serializerSettingsResolver));

            _streamStore = streamStore;
            _entityAccessor = entityAccessor;
            _differ = new JsonDiffPatch();
            _jsonSerializer = JsonSerializer.Create(serializerSettingsResolver.ResolveSettings(this));

            _lookup = new Dictionary<(string bucket, string id, long requestedRevision), (object entity, long revision)>();
        }

        #endregion

        public IEnumerable<(Type type, string id, long revision, object entity)> LoadedEntries =>
            _lookup.Where(p => p.Key.requestedRevision == default)
                   .Select(p => (type: GetTypeFromBucket(p.Key.bucket), p.Key.id, p.Value.revision, p.Value.entity));

        private static JToken StreamRoot => JToken.Parse("null");

        #region IEntityStorageEngine

        public ValueTask<object> GetByIdAsync(Type entityType, string id, CancellationToken cancellation)
        {
            return GetByIdAsync(entityType, id, revision: default, cancellation: cancellation);
        }

        public ValueTask<object> GetByIdAsync(Type entityType, string id, long revision, CancellationToken cancellation)
        {
            if (entityType == null)
                throw new ArgumentNullException(nameof(entityType));

            if (entityType.IsValueType)
                throw new ArgumentException("The argument must specify a reference type.", nameof(entityType));

            return CachedDeserializeAsync(entityType, id, revision, cancellation);
        }

        public async ValueTask<(object entity, long revision)> LoadEntityAsync(Type entityType, string id, CancellationToken cancellation = default)
        {
            var bucketId = GetBucketId(entityType);

            if (!_lookup.TryGetValue((bucketId, id, requestedRevision: default), out var result))
            {
                var stream = await _streamStore.OpenStreamAsync(bucketId, id, revision: default, cancellation);
                var entity = Deserialize(entityType, stream);
                result = (entity, revision: stream.StreamRevision);

                _lookup[(bucketId, stream.StreamId, requestedRevision: default)] = result;
            }

            return result;
        }

        public IAsyncEnumerable<object> GetAllAsync(Type entityType, CancellationToken cancellation)
        {
            if (entityType == null)
                throw new ArgumentNullException(nameof(entityType));

            if (entityType.IsValueType)
                throw new ArgumentException("The argument must specify a reference type.", nameof(entityType));

            var bucketId = GetBucketId(entityType);

            return _streamStore.OpenAllAsync(bucketId, cancellation).Select(stream => CachedDeserialize(entityType, revision: default, stream));
        }

        public IAsyncEnumerable<object> GetAllAsync(CancellationToken cancellation)
        {
            return _streamStore.OpenAllAsync(cancellation).Select(stream => CachedDeserialize(GetTypeFromBucket(stream.BucketId), revision: default, stream));
        }

        public async Task StoreAsync(Type entityType, object entity, string id, CancellationToken cancellation)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (id == null)
                throw new ArgumentNullException(nameof(id));

            if (entityType == null)
                throw new ArgumentNullException(nameof(entityType));

            if (entityType.IsValueType)
                throw new ArgumentException("The argument must specify a reference type.", nameof(entityType));

            if (!entityType.IsAssignableFrom(entity.GetType()))
                throw new ArgumentException($"The specified entity must be of type '{entityType.FullName}' or a derived type.", nameof(entity));

            var bucketId = GetBucketId(entityType);
            var streamId = id;
            var stream = await _streamStore.OpenStreamAsync(bucketId, streamId, throwIfNotFound: false, cancellation);

            var (concurrencyToken, events) = GetEntityProperties(entity);
            var commitBody = BuildCommitBody(entity, stream);

            void HeaderGenerator(IDictionary<string, object> headers) { }

            await stream.CommitAsync(concurrencyToken,
                                     events,
                                     commitBody,
                                     HeaderGenerator,
                                     cancellation);

            _entityAccessor.SetConcurrencyToken(entity, stream.ConcurrencyToken);
            _entityAccessor.SetRevision(entity, stream.StreamRevision);
            _entityAccessor.CommitEvents(entity);

            _lookup[(bucketId, streamId, requestedRevision: default)] = (entity, revision: stream.StreamRevision);
        }

        public async Task DeleteAsync(Type entityType, object entity, string id, CancellationToken cancellation)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (id == null)
                throw new ArgumentNullException(nameof(id));

            if (entityType == null)
                throw new ArgumentNullException(nameof(entityType));

            if (entityType.IsValueType)
                throw new ArgumentException("The argument must specify a reference type.", nameof(entityType));

            if (!entityType.IsAssignableFrom(entity.GetType()))
                throw new ArgumentException($"The specified entity must be of type '{entityType.FullName}' or a derived type.", nameof(entity));

            var bucketId = GetBucketId(entityType);
            var streamId = id;
            var stream = await _streamStore.OpenStreamAsync(bucketId, streamId, throwIfNotFound: false, cancellation);

            var (concurrencyToken, events) = GetEntityProperties(entity);
            var commitBody = BuildCommitBody(entity: null, stream);

            void HeaderGenerator(IDictionary<string, object> headers) { }

            await stream.CommitAsync(concurrencyToken,
                                     events,
                                     commitBody,
                                     HeaderGenerator,
                                     cancellation);

            // TODO: Do we set the properties?
            //_entityAccessor.SetConcurrencyToken(entity, stream.ConcurrencyToken);
            //_entityAccessor.SetRevision(entity, stream.StreamRevision);
            //_entityAccessor.CommitEvents(entity);

            _lookup[(bucketId, streamId, requestedRevision: default)] = (null, revision: stream.StreamRevision);
        }

        #endregion

        private (string concurrencyToken, IEnumerable<EventMessage> events) GetEntityProperties(object entity)
        {
            var concurrencyToken = _entityAccessor.GetConcurrencyToken(entity);
            var events = _entityAccessor.GetUncommittedEvents(entity).Select(p => new EventMessage { Body = p });

            return (concurrencyToken, events);
        }

        private byte[] BuildCommitBody(object entity, IStream stream)
        {
            var baseToken = GetBaseToken(stream);
            var serializedEntity = GetSerializedEntity(entity);
            var diff = _differ.Diff(baseToken, serializedEntity);
            return CompressionHelper.Zip(diff.ToString());
        }

        private JToken GetSerializedEntity(object entity)
        {
            if (entity == null)
            {
                return StreamRoot;
            }

            return JToken.FromObject(entity, _jsonSerializer);
        }

        private JToken GetBaseToken(IStream stream)
        {
            Assert(stream != null);

            JToken baseToken;
            if (stream.Snapshot == null)
            {
                baseToken = StreamRoot;
            }
            else
            {
                var snapshotPayload = stream.Snapshot.Payload as byte[];

                baseToken = JToken.Parse(CompressionHelper.Unzip(snapshotPayload));
            }

            foreach (var commit in stream.Commits)
            {
                var commitPayload = commit.Body as byte[];

                baseToken = _differ.Patch(baseToken, JToken.Parse(CompressionHelper.Unzip(commitPayload)));
            }

            return baseToken;
        }

        private object Deserialize(Type entityType, IStream stream)
        {
            // This is an empty stream.
            if (stream.StreamRevision == default)
                return null;

            var serializedEntity = default(JToken);

            if (stream.Snapshot == null)
            {
                serializedEntity = StreamRoot;
            }
            else
            {
                serializedEntity = JToken.Parse(CompressionHelper.Unzip(stream.Snapshot.Payload as byte[]));
            }

            foreach (var commit in stream.Commits)
            {
                serializedEntity = _differ.Patch(serializedEntity, JToken.Parse(CompressionHelper.Unzip(commit.Body as byte[])));
            }

            var result = serializedEntity.ToObject(entityType, _jsonSerializer);

            if (result != null)
            {
                _entityAccessor.SetConcurrencyToken(result, stream.ConcurrencyToken);
                _entityAccessor.SetRevision(result, stream.StreamRevision);
                _entityAccessor.CommitEvents(result);
            }

            return result;
        }

        private object CachedDeserialize(Type entityType, long revision, IStream stream)
        {
            var bucketId = GetBucketId(entityType);

            if (_lookup.TryGetValue((bucketId, stream.StreamId, revision), out var cachedResult))
            {
                return cachedResult;
            }

            Assert(revision != default, stream.StreamRevision == revision);
            var entity = Deserialize(entityType, stream);

            _lookup[(bucketId, stream.StreamId, revision)] = (entity, revision: stream.StreamRevision);

            return entity;
        }

        private async ValueTask<object> CachedDeserializeAsync(Type entityType,
                                                               string id,
                                                               long revision,
                                                               CancellationToken cancellation)
        {
            var bucketId = GetBucketId(entityType);

            if (_lookup.TryGetValue((bucketId, id, revision), out var cachedResult))
            {
                return cachedResult;
            }

            var stream = await _streamStore.OpenStreamAsync(bucketId, id, revision, cancellation);

            Assert(revision != default, stream.StreamRevision == revision);
            var entity = Deserialize(entityType, stream);

            _lookup[(bucketId, stream.StreamId, revision)] = (entity, revision: stream.StreamRevision);

            return entity;
        }

        private static string GetBucketId(Type entityType)
        {
            return entityType.ToString();
        }

        private static Type GetTypeFromBucket(string bucketId)
        {
            return TypeLoadHelper.LoadTypeFromUnqualifiedName(bucketId);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _streamStore.Dispose();
        }
    }
}