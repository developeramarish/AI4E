﻿/* Summary
 * --------------------------------------------------------------------------------------------------------------------
 * Filename:        IServerEndPoint.cs 
 * Types:           (1) AI4E.Routing.SignalR.Server.IServerEndPoint
 * Version:         1.0
 * Author:          Andreas Trütschel
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
using System.Threading;
using System.Threading.Tasks;
using AI4E.Remoting;

namespace AI4E.Routing.SignalR.Server
{
    /// <summary>
    /// Represents the server end-point, directly wrapping an underlying signal-r connection.
    /// </summary>
    public interface IServerEndPoint : IDisposable
    {
        /// <summary>
        /// Asynchronously received a message from any connected clients.
        /// </summary>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>
        /// A task representing the asynchronous operation.
        /// When evaluated, the tasks result contains the received message and the address of the client the sent the message.</returns>
        Task<(IMessage message, string address)> ReceiveAsync(CancellationToken cancellation = default);

        /// <summary>
        /// Asynchronously sends a message to the specified client.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="address">The address of the client.</param>
        /// <param name="cancellation">A <see cref="CancellationToken"/> used to cancel the asynchronous operation or <see cref="CancellationToken.None"/>.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="ArgumentNullException">Thrown if any of <paramref name="message"/> or <paramref name="address"/> is null</exception>
        Task SendAsync(IMessage message, string address, CancellationToken cancellation = default);
    }
}