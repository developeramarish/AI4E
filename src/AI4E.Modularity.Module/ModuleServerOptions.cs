﻿namespace AI4E.Modularity
{
    public sealed class ModuleServerOptions
    {
        public string Prefix { get; set; }

        public bool UseDebugConnection { get; set; } = false;

        public string DebugConnection { get; set; } = "localhost:8080";
    }
}
