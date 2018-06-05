﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AI4E.Storage.Transactions
{
    public interface ITransactionStateStorage
    {
        ValueTask<IEnumerable<ITransactionState>> GetNonCommittedTransactionsAsync(CancellationToken cancellation = default);

        ValueTask<ITransactionState> GetTransactionAsync(long id, CancellationToken cancellation = default);

        ValueTask<ITransactionState> GetLatestTransactionAsync(long minId = default, CancellationToken cancellation = default);

        Task RemoveAsync(ITransactionState transaction, CancellationToken cancellation = default);

        ValueTask<bool> CompareExchangeAsync(ITransactionState transaction, ITransactionState comparand, CancellationToken cancellation = default);
    }
}
