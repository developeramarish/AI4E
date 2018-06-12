﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Internal;
using AI4E.Async;
using static System.Diagnostics.Debug;

namespace AI4E.Storage.Projection
{
    public sealed class Projector : IProjector
    {
        private readonly IServiceProvider _serviceProvider;
        private TypedProjectorLookup _typedProjectors;

        public Projector(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            _serviceProvider = serviceProvider;

            _typedProjectors = new TypedProjectorLookup(serviceProvider);
        }

        public IHandlerRegistration<IProjection<TSource, TProjection>> RegisterProjection<TSource, TProjection>(
            IContextualProvider<IProjection<TSource, TProjection>> projectionProvider)
            where TSource : class
            where TProjection : class
        {
            if (projectionProvider == null)
                throw new ArgumentNullException(nameof(projectionProvider));

            return _typedProjectors.GetProjector<TSource, TProjection>()
                                   .RegisterProjection(projectionProvider);
        }

        public async Task<IEnumerable<IProjectionResult>> ProjectAsync(Type sourceType,
                                                                       object source,
                                                                       CancellationToken cancellation)
        {
            if (sourceType == null)
                throw new ArgumentNullException(nameof(sourceType));

            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (sourceType.IsValueType)
                throw new ArgumentException("The argument must be a reference type.", nameof(sourceType));

            if (!sourceType.IsAssignableFrom(source.GetType()))
                throw new ArgumentException($"The argument '{nameof(source)}' must be of the type specified by '{nameof(sourceType)}' or a derived type.");

            var typedProjectors = _typedProjectors.GetProjectors(sourceType);
            var result = new List<IProjectionResult>();

            // There is no parallelism (with Task.WhenAll(projectors.Select(...)) used because we cannot guarantee that it is allowed to access 'source' concurrently.
            // TODO: But it is possible to change the return type to IAsyncEnumerable<IProjectionResult> and process each batch on access. 
            //       This allows to remove the up-front evaluation and storage of the results.
            foreach (var typedProjector in typedProjectors)
            {
                result.AddRange(await typedProjector.ProjectAsync(source, cancellation));
            }

            return result;
        }

        public Task<IEnumerable<IProjectionResult>> ProjectAsync<TSource>(TSource source, CancellationToken cancellation)
            where TSource : class
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (!(source is TSource typedSource))
            {
                throw new ArgumentException($"The argument must be of type '{ typeof(TSource).FullName }' or a derived type.", nameof(source));
            }

            return ProjectAsync(typeof(TSource), source, cancellation);
        }

        private interface ITypedProjector
        {
            Task<IEnumerable<IProjectionResult>> ProjectAsync(object source, CancellationToken cancellation);

            Type SourceType { get; }

            Type ProjectionType { get; }
        }

        private interface ITypedProjector<TSource> : ITypedProjector
            where TSource : class
        {
            Task<IEnumerable<IProjectionResult>> ProjectAsync(TSource source, CancellationToken cancellation);
        }

        private interface ITypedProjector<TSource, TProjection> : ITypedProjector<TSource>
            where TSource : class
            where TProjection : class
        {
            IHandlerRegistration<IProjection<TSource, TProjection>> RegisterProjection(
                IContextualProvider<IProjection<TSource, TProjection>> projectionProvider);
        }

        private sealed class TypedProjector<TSource, TProjectionId, TProjection> : ITypedProjector<TSource, TProjection>
            where TSource : class
            where TProjectionId : struct, IEquatable<TProjectionId>
            where TProjection : class

        {
            private readonly HandlerRegistry<IProjection<TSource, TProjection>> _projections;
            private readonly IServiceProvider _serviceProvider;

            public TypedProjector(IServiceProvider serviceProvider)
            {
                Assert(serviceProvider != null);
                _serviceProvider = serviceProvider;
            }

            public Type SourceType => typeof(TSource);
            public Type ProjectionType => typeof(TProjection);

            // There is no parallelism (with Task.WhenAll(projections.Select(...)) used because we cannot guarantee that it is allowed to access 'source' concurrently.
            // TODO: But it is possible to change the return type to IAsyncEnumerable<IProjectionResult> and process each batch on access. 
            //       This allows to remove the up-front evaluation and storage of the results.
            public async Task<IEnumerable<IProjectionResult>> ProjectAsync(TSource source, CancellationToken cancellation)
            {
                Assert(source != null);

                var result = new List<IProjectionResult<TProjectionId, TProjection>>();

                foreach (var projectionProvider in _projections.Handlers)
                {
                    var projection = projectionProvider.ProvideInstance(_serviceProvider);

                    Assert(projection != null);

                    if (projection.Multiple)
                    {
                        result.AddRange(await projection.ProjectMultipleAsync(source)
                                                        .Where(p => p != null)
                                                        .Select(p => new ProjectionResult<TProjectionId, TProjection>(p)));
                    }
                    else
                    {
                        var res = await projection.ProjectAsync(source);

                        if (res != null)
                        {
                            result.Add(new ProjectionResult<TProjectionId, TProjection>(res));
                        }
                    }
                }

                return result;
            }

            public Task<IEnumerable<IProjectionResult>> ProjectAsync(object source, CancellationToken cancellation)
            {
                Assert(source != null);
                Assert(source is TSource);

                return ProjectAsync(source as TSource, cancellation);
            }

            public IHandlerRegistration<IProjection<TSource, TProjection>> RegisterProjection(IContextualProvider<IProjection<TSource, TProjection>> projectionProvider)
            {
                Assert(projectionProvider != null);
                return HandlerRegistration.CreateRegistration(_projections, projectionProvider);
            }
        }

        private sealed class ProjectionResult<TProjectionId, TProjection> : IProjectionResult<TProjectionId, TProjection>
            where TProjectionId : struct, IEquatable<TProjectionId>
            where TProjection : class
        {
            public ProjectionResult(TProjection result)
            {
                Assert(result != null);

                Result = result;
                ResultId = DataPropertyHelper.GetId<TProjectionId, TProjection>(result);
            }

            public TProjectionId ResultId { get; }

            public Type ResultIdType => typeof(TProjectionId);

            public TProjection Result { get; }

            public Type ResultType => typeof(TProjection);

            object IProjectionResult.ResultId => ResultId;

            object IProjectionResult.Result => Result;
        }

        private sealed class TypedProjectorLookup
        {
            private static readonly Type _typedProjectorTypeDefintion = typeof(TypedProjector<,,>);

            private readonly object _lock = new object();
            private readonly Dictionary<(Type sourceType, Type projectionType), object> _projectors;
            private readonly Dictionary<Type, ImmutableList<object>> _sourceProjectors;
            private readonly IServiceProvider _serviceProvider;

            public TypedProjectorLookup(IServiceProvider serviceProvider)
            {
                Assert(serviceProvider != null);
                _serviceProvider = serviceProvider;

                _projectors = new Dictionary<(Type sourceType, Type projectionType), object>();
                _sourceProjectors = new Dictionary<Type, ImmutableList<object>>();
            }

            public bool TryGetProjector<TSource, TProjection>(out ITypedProjector<TSource, TProjection> projector)
                where TSource : class
                where TProjection : class
            {
                bool result;
                object untypedProjector;

                lock (_lock)
                {
                    result = _projectors.TryGetValue((typeof(TSource), typeof(TProjection)), out untypedProjector);
                }

                if (!result)
                {
                    projector = null;
                    return false;
                }

                projector = untypedProjector as ITypedProjector<TSource, TProjection>;

                Assert(projector != null);

                return true;
            }

            public ITypedProjector<TSource, TProjection> GetProjector<TSource, TProjection>()
                where TSource : class
                where TProjection : class
            {
                object projector;
                bool found;

                lock (_lock)
                {
                    found = _projectors.TryGetValue((typeof(TSource), typeof(TProjection)), out projector);
                }

                if (!found)
                {
                    projector = CreateProjector<TSource, TProjection>();

                    lock (_lock)
                    {
                        if (_projectors.TryGetValue((typeof(TSource), typeof(TProjection)), out var p))
                        {
                            projector = p;
                        }
                        else
                        {
                            _projectors.Add((typeof(TSource), typeof(TProjection)), projector);

                            if (!_sourceProjectors.TryGetValue(typeof(TSource), out var sourceProjectors))
                            {
                                sourceProjectors = ImmutableList<object>.Empty;
                            }

                            _sourceProjectors[typeof(TSource)] = sourceProjectors.Add(projector);
                        }
                    }
                }

                Assert(projector != null);
                var result = projector as ITypedProjector<TSource, TProjection>;
                Assert(result != null);
                return result;
            }

            public IEnumerable<ITypedProjector<TSource>> GetProjectors<TSource>()
                where TSource : class
            {
                var sourceProjectors = GetProjectorsInternal(typeof(TSource));
                return sourceProjectors.Cast<ITypedProjector<TSource>>();
            }

            private ImmutableList<object> GetProjectorsInternal(Type sourceType)
            {
                ImmutableList<object> sourceProjectors;

                lock (_lock)
                {
                    if (!_sourceProjectors.TryGetValue(sourceType, out sourceProjectors))
                    {
                        sourceProjectors = ImmutableList<object>.Empty;
                    }
                }

                Assert(sourceProjectors != null);
                return sourceProjectors;
            }

            public IEnumerable<ITypedProjector> GetProjectors(Type sourceType)
            {
                Assert(sourceType != null);
                Assert(!sourceType.IsValueType);

                var sourceProjectors = GetProjectorsInternal(sourceType);
                return sourceProjectors.Cast<ITypedProjector>();
            }

            private ITypedProjector<TSource, TProjection> CreateProjector<TSource, TProjection>()
                where TSource : class
                where TProjection : class
            {
                var sourceType = typeof(TSource);
                var projectionType = typeof(TProjection);
                var projectionIdType = DataPropertyHelper.GetIdType(projectionType);
                var projectorType = MakeProjectorType(sourceType, projectionType, projectionIdType);
                var typedProjector = Activator.CreateInstance(projectorType, _serviceProvider);

                Assert(typedProjector != null);

                return (ITypedProjector<TSource, TProjection>)typedProjector;
            }

            private static Type MakeProjectorType(Type sourceType, Type projectionType, Type projectionIdType)
            {
                return _typedProjectorTypeDefintion.MakeGenericType(sourceType,
                                                                    projectionIdType,
                                                                    projectionType);
            }
        }
    }
}
