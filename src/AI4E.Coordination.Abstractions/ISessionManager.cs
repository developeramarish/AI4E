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
using System.Threading;
using System.Threading.Tasks;

namespace AI4E.Coordination
{
    public interface ISessionManager
    {
        Task<bool> TryBeginSessionAsync(Session session, DateTime leaseEnd, CancellationToken cancellation = default);

        Task UpdateSessionAsync(Session session, DateTime leaseEnd, CancellationToken cancellation = default);

        Task AddSessionEntryAsync(Session session, CoordinationEntryPath entryPath, CancellationToken cancellation = default);
        Task RemoveSessionEntryAsync(Session session, CoordinationEntryPath entryPath, CancellationToken cancellation = default);
        Task<IEnumerable<CoordinationEntryPath>> GetEntriesAsync(Session session, CancellationToken cancellation = default);

        Task EndSessionAsync(Session session, CancellationToken cancellation = default);

        Task WaitForTerminationAsync(Session session, CancellationToken cancellation = default);

        /// <summary>
        /// Asynchronously await a session to terminate and provides the terminated sessions identifier.
        /// </summary>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// A task representing the asynchronous operation.
        /// When evaluated, the tasks result contains the identifier of the the terminated session.
        /// </returns>
        /// <exception cref="OperationCanceledException">Thrown if the operation was canceled.</exception>
        Task<Session> WaitForTerminationAsync(CancellationToken cancellation = default);

        Task<bool> IsAliveAsync(Session session, CancellationToken cancellation = default);

        IAsyncEnumerable<Session> GetSessionsAsync(CancellationToken cancellation = default);
    }
}
