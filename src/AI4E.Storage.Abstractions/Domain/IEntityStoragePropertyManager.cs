﻿using System.Collections.Generic;

namespace AI4E.Storage.Domain
{
    // TODO: Rename
    // The service must be registered with the same scope than the storage-engine to ensure consistency.
    public interface IEntityStoragePropertyManager
    {
        string GetConcurrencyToken(object entity);
        void SetConcurrencyToken(object entity, string concurrencyToken);

        long GetRevision(object entity);
        void SetRevision(object entity, long revision);

        void CommitEvents(object entity);
        IEnumerable<object> GetUncommittedEvents(object entity);
    }
}
