﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AI4E.Modularity.Host
{
    public interface IModuleRelease
    {
        string Author { get; }
        IEnumerable<ModuleDependency> Dependencies { get; }
        string Description { get; }
        ModuleReleaseIdentifier Id { get; }
        bool IsInstalled { get; }
        IModule Module { get; }
        string Name { get; }
        DateTime ReleaseDate { get; }
        ModuleVersion Version { get; }

        ValueTask<IEnumerable<IModuleSource>> GetSourcesAsync(CancellationToken cancellation);

        void AddSource(IModuleSource source);
        void Install();
        void RemoveSource(IModuleSource source);
        void Uninstall();
    }
}