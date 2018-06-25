﻿/* License
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

namespace AI4E.Domain
{
    public abstract class AggregateRootBase : EntityBase
    {
        private readonly List<DomainEventBase> _uncommittedEvents = new List<DomainEventBase>();

        protected AggregateRootBase(string id) : base(id) { }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                DoDispose();
                IsDisposed = true;
            }
        }

        public string ConcurrencyToken { get; internal set; }

        public long Revision { get; internal set; }

        protected virtual void DoDispose() { }

        protected void Notify<TEvent>(TEvent evt)
            where TEvent : DomainEventBase
        {
            ThrowIfDisposed();

            if (evt == null)
                throw new ArgumentNullException(nameof(evt));

            if (evt.AggregateId != Id)
                throw new ArgumentException("The event does not belong to the stream of the aggregate.", nameof(evt));

            _uncommittedEvents.Add(evt);
        }

        internal IEnumerable<DomainEventBase> UncommittedEvents => _uncommittedEvents;

        internal void CommitEvents()
        {
            _uncommittedEvents.Clear();
        }

        protected virtual void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(EntityType.FullName);
        }

        protected T ThrowIfDisposed<T>(T value)
        {
            ThrowIfDisposed();
            return value;
        }

        protected T ThrowIfDisposed<T>(Func<T> factory)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            ThrowIfDisposed();
            return factory();
        }
    }
}