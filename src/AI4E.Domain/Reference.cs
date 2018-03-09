﻿/* Summary
 * --------------------------------------------------------------------------------------------------------------------
 * Filename:        Reference.cs 
 * Types:           (1) AI4E.Domain.Reference'1
 * Version:         1.0
 * Author:          Andreas Trütschel
 * Last modified:   18.10.2017 
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
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace AI4E.Domain
{
    /// <summary>
    /// Repesents a reference to an aggregate that can be resolved.
    /// </summary>
    /// <typeparam name="T">The type of aggregate root.</typeparam>
    public readonly struct Reference<T> : IEquatable<Reference<T>>
        where T : AggregateRoot
    {
        private static readonly int _typeHashCode = typeof(T).GetHashCode();

        private readonly AsyncLazy<T> _aggregate;
        private readonly Guid _id;

        /// <summary>
        /// Creates a reference to the specified aggregate.
        /// </summary>
        /// <param name="aggregate">The aggregate to reference.</param>
        public Reference(T aggregate)
        {
            if (aggregate == null)
            {
                _id = Guid.Empty;
            }
            else
            {
                if (aggregate.Id == Guid.Empty)
                {
                    throw new ArgumentException("Cannot get a reference to an aggregate without an id specified.");
                }

                _id = aggregate.Id;
            }

            _aggregate = new AsyncLazy<T>(() => Task.FromResult(aggregate));
        }

        [MethodImpl(MethodImplOptions.PreserveSig)]
        private Reference(Guid id, IReferenceResolver referenceResolver)
        {
            if (referenceResolver == null)
                throw new ArgumentNullException(nameof(referenceResolver));

            _id = id;

            if (id != Guid.Empty)
            {
                _aggregate = new AsyncLazy<T>(async () => await referenceResolver.ResolveAsync<T>(id, cancellation: default));
            }
            else
            {
                _aggregate = new AsyncLazy<T>(() => Task.FromResult<T>(null));
            }
        }

        public Guid Id => _id;

        /// <summary>
        /// Asynchronously resolves the reference and provides an instance of the referenced aggregate.
        /// </summary>
        /// <returns>A task representing the asnychronous operation.</returns>
        public Task<T> ResolveAsync() // TODO: Cancellation support?
        {
            return _aggregate.Task;
        }

        #region Equality

        public bool Equals(Reference<T> other)
        {
            return other.Id == Id;
        }

        public override bool Equals(object obj)
        {
            return obj is Reference<T> reference && Equals(reference);
        }

        public static bool operator ==(Reference<T> left, Reference<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Reference<T> left, Reference<T> right)
        {
            return !left.Equals(right);
        }

        #endregion

        public override int GetHashCode()
        {
            return _typeHashCode ^ Id.GetHashCode();
        }

        public override string ToString()
        {
            return $"{typeof(T).FullName} #{Id}";
        }

        public static implicit operator Reference<T>(T aggregate)
        {
            return new Reference<T>(aggregate);
        }
    }

    public static class ReferenceExtension
    {
        public static TaskAwaiter<T> GetAwaiter<T>(in this Reference<T> reference)
            where T : AggregateRoot
        {
            return reference.ResolveAsync().GetAwaiter();
        }

        public static async Task<IEnumerable<T>> ResolveAsync<T>(this IEnumerable<Reference<T>> references)
             where T : AggregateRoot
        {
            return await Task.WhenAll(references.Select(p => p.ResolveAsync()));
        }

        public static TaskAwaiter<IEnumerable<T>> GetAwaiter<T>(this IEnumerable<Reference<T>> references)
            where T : AggregateRoot
        {
            return references.ResolveAsync().GetAwaiter();
        }
    }

    /// <summary>
    /// References a snapshot of an aggregate. 
    /// </summary>
    /// <typeparam name="T">The type of aggregate root.</typeparam>
    public readonly struct Snapshot<T> : IEquatable<Snapshot<T>>
        where T : AggregateRoot
    {
        private static readonly int _typeHashCode = typeof(T).GetHashCode();

        private readonly AsyncLazy<T> _aggregate;
        private readonly Guid _id;
        private readonly long _revision;

        public Snapshot(T aggregate)
        {
            if (aggregate == default)
            {
                _id = default;
                _revision = default;
            }
            else
            {
                if (aggregate.Id == default)
                {
                    throw new ArgumentException("Cannot get a reference to an aggregate without an id specified.");
                }

                _id = aggregate.Id;
                _revision = aggregate.Revision;
            }

            _aggregate = new AsyncLazy<T>(() => Task.FromResult(aggregate));
        }

        [MethodImpl(MethodImplOptions.PreserveSig)]
        private Snapshot(Guid id, long revision, IReferenceResolver referenceResolver)
        {
            if (referenceResolver == null)
                throw new ArgumentNullException(nameof(referenceResolver));

            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            _id = id;
            _revision = revision;

            if (id != default)
            {
                _aggregate = new AsyncLazy<T>(async () => await referenceResolver.ResolveAsync<T>(id, revision, cancellation: default));
            }
            else
            {
                _aggregate = new AsyncLazy<T>(() => Task.FromResult<T>(null));
            }
        }

        public Guid Id => _id;
        public long Revision => _revision;

        /// <summary>
        /// Asynchronously resolves the reference and provides an instance of the referenced aggregate.
        /// </summary>
        /// <returns>A task representing the asnychronous operation.</returns>
        public Task<T> ResolveAsync()
        {
            return _aggregate.Task;
        }

        #region Equality

        public bool Equals(Snapshot<T> other)
        {
            return other.Id == Id && other.Revision == Revision;
        }

        public override bool Equals(object obj)
        {
            return obj is Snapshot<T> snapshot && Equals(snapshot);
        }

        public static bool operator ==(Snapshot<T> left, Snapshot<T> right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Snapshot<T> left, Snapshot<T> right)
        {
            return !left.Equals(right);
        }

        #endregion

        public override int GetHashCode()
        {
            return _typeHashCode ^ Id.GetHashCode();
        }

        public override string ToString()
        {
            return $"{typeof(T).FullName} #{Id} {Revision}";
        }

        public static implicit operator Snapshot<T>(T aggregate)
        {
            return new Snapshot<T>(aggregate);
        }
    }
}
