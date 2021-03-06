using AI4E.Modularity;

namespace AI4E.Blazor.Modularity
{
    internal sealed class LookupModulePrefix
    {
        public LookupModulePrefix(ModuleIdentifier module)
        {
            Module = module;
        }

        public ModuleIdentifier Module { get; }
    }
}
