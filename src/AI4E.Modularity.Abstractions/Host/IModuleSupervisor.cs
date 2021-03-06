﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Utils.Async;

namespace AI4E.Modularity.Host
{
    public interface IModuleSupervisor : IAsyncDisposable
    {
        DirectoryInfo Directory { get; }
        ModuleSupervisorState State { get; }

        Task<ModuleReleaseIdentifier> GetSupervisedModule(CancellationToken cancellation);

        event EventHandler<ModuleSupervisorState> StateChanged;
    }

    public enum ModuleSupervisorState
    {
        Initializing,
        Running,
        NotRunning,
        Failed,
        Shutdown
    }
}