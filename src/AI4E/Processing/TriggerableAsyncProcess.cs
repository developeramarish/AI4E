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
using System.Threading;
using System.Threading.Tasks;
using AI4E.Internal;

namespace AI4E.Processing
{
    public sealed class TriggerableAsyncProcess : IAsyncProcess, ITriggerableAsyncProcess
    {
        #region Fields

        private readonly IAsyncProcess _dynamicProcess;
        private readonly AsyncProcessScheduler _scheduler = new AsyncProcessScheduler();
        private readonly Func<CancellationToken, Task> _operation;
        private int _operating = 0; // 0 = Idle, 1 = Running

        #endregion

        /// <summary>
        /// Creates a new instance of the <see cref="Process"/> type with the specified execution operation.
        /// </summary>
        /// <param name="operation">The asynchronous execution operation.</param>
        public TriggerableAsyncProcess(Func<CancellationToken, Task> operation) // The operation is guaranteed not to run concurrently.
        {
            if (operation == null)
                throw new ArgumentNullException(nameof(operation));

            _operation = operation;
            _dynamicProcess = new AsyncProcess(DynamicExecute);
        }

        #region Properties

        /// <summary>
        /// Gets a task representing the process execution operation.
        /// </summary>
        [Obsolete]
        public Task Execution => _dynamicProcess.Execution;

        [Obsolete]
        public Task Initialization => _dynamicProcess.Initialization;

        [Obsolete]
        public Task Termination => _dynamicProcess.Termination;

        /// <summary>
        /// Gets the state of the process.
        /// </summary>
        public TriggerableAsyncProcessState State => (TriggerableAsyncProcessState)((int)_dynamicProcess.State & (Volatile.Read(ref _operating) << 4));

        AsyncProcessState IAsyncProcess.State => _dynamicProcess.State;

        #endregion

        /// <summary>
        /// Starts the dynamic process operation.
        /// </summary>
        [Obsolete]
        public void Start()
        {
            _dynamicProcess.Start();
        }

        public Task StartAsync()
        {
            return _dynamicProcess.StartAsync();
        }

        /// <summary>
        /// Terminates the dynamic process operation.
        /// </summary>
        [Obsolete]
        public void Terminate()
        {
            _dynamicProcess.Terminate();
        }

        public Task TerminateAsync()
        {
            return _dynamicProcess.TerminateAsync();
        }
     
        public void TriggerExecution()
        {
            _scheduler.Trigger();
        }

        /// <summary>
        /// Registers a dynamic process trigger.
        /// </summary>
        /// <param name="trigger">The trigger that shall be registered.</param>
        public void RegisterTrigger(ITrigger trigger)
        {
            _scheduler.AddTrigger(trigger);
        }

        /// <summary>
        /// Unregisteres a dyanamic process trigger.
        /// </summary>
        /// <param name="trigger">The trigger that shall be unregistered.</param>
        public void UnregisterTrigger(ITrigger trigger)
        {
            _scheduler.RemoveTrigger(trigger);
        }

        private async Task DynamicExecute(CancellationToken cancellation)
        {
            while (cancellation.ThrowOrContinue())
            {
                await _scheduler.NextTrigger();

                await StaticExecute(cancellation);
            }
        }

        private async Task StaticExecute(CancellationToken cancellation)
        {
            if (Interlocked.Exchange(ref _operating, 1) != 0)
                return;

            try
            {
                await _operation(cancellation);
            }
            finally
            {
                Volatile.Write(ref _operating, 0);
            }
        }
    }
}
