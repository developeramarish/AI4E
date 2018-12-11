using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Modularity.Host;
using AI4E.Modularity.Hosting.Sample.Api;
using AI4E.Modularity.Hosting.Sample.Models;
using AI4E.Storage;
using Microsoft.AspNetCore.Mvc;

namespace AI4E.Modularity.Hosting.Sample.Services
{
    public sealed class ModuleQueryHandler : MessageHandler
    {
        public async Task<IEnumerable<ModuleListModel>> HandleAsync(
            ModuleSearchQuery query,
            [FromServices][Inject] IModuleSearchEngine searchEngine,
            CancellationToken cancellation)
        {
            var modules = await searchEngine.SearchModulesAsync(query.SearchPhrase, query.IncludePreReleases, cancellation);
            var projection = new ModuleProjection();

            return modules.Select(p => projection.ProjectToListModel(p, query.IncludePreReleases)).ToList();
        }

        public ValueTask<ModuleReleaseModel> HandleAsync(
            ByIdQuery<ModuleReleaseIdentifier, ModuleReleaseModel> query,
            [FromServices][Inject] IDatabase database,
            CancellationToken cancellation)
        {
            return database.GetOneAsync<ModuleReleaseModel>(p => p.Id == query.Id, cancellation);
        }

        public ValueTask<ModuleInstallModel> HandleAsync(
            ByIdQuery<ModuleReleaseIdentifier, ModuleInstallModel> query,
            [FromServices][Inject] IDatabase database,
            CancellationToken cancellation)
        {
            return database.GetOneAsync<ModuleInstallModel>(p => p.Id == query.Id, cancellation);
        }

        public ValueTask<ModuleUninstallModel> HandleAsync(
            ByIdQuery<ModuleIdentifier, ModuleUninstallModel> query,
            [FromServices][Inject] IDatabase database,
            CancellationToken cancellation)
        {
            return database.GetOneAsync<ModuleUninstallModel>(p => p.Id == query.Id, cancellation);
        }
    }
}
