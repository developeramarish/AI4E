using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AI4E.Modularity.Hosting.Sample.Models;
using AI4E.Storage;

namespace AI4E.Modularity.Hosting.Sample.Services
{
    public sealed class ModuleSourceQueryHandler : MessageHandler
    {
        private readonly IDatabase _database;

        public ModuleSourceQueryHandler(IDatabase database)
        {
            _database = database;
        }

        public async Task<IEnumerable<ModuleSourceListModel>> HandleAsync(Query<IEnumerable<ModuleSourceListModel>> query, CancellationToken cancellation)
        {
            return await _database.GetAsync<ModuleSourceListModel>(cancellation).ToArray();
        }

        public ValueTask<ModuleSourceModel> HandleAsync(ByIdQuery<ModuleSourceModel> query, CancellationToken cancellation)
        {
            return _database.GetOneAsync<ModuleSourceModel>(p => p.Id == query.Id, cancellation);
        }

        public ValueTask<ModuleSourceDeleteModel> HandleAsync(ByIdQuery<ModuleSourceDeleteModel> query, CancellationToken cancellation)
        {
            return _database.GetOneAsync<ModuleSourceDeleteModel>(p => p.Id == query.Id, cancellation);
        }

        public ValueTask<ModuleSourceRenameModel> HandleAsync(ByIdQuery<ModuleSourceRenameModel> query, CancellationToken cancellation)
        {
            return _database.GetOneAsync<ModuleSourceRenameModel>(p => p.Id == query.Id, cancellation);
        }

        public ValueTask<ModuleSourceUpdateLocationModel> HandleAsync(ByIdQuery<ModuleSourceUpdateLocationModel> query, CancellationToken cancellation)
        {
            return _database.GetOneAsync<ModuleSourceUpdateLocationModel>(p => p.Id == query.Id, cancellation);
        }
    }
}
