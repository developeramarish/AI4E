﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AI4E.Storage.Transactions
{
    public interface ITransactionManager : IDisposable
    {
        //IScopedTransactionalDatabase CreateStore();

        ValueTask<ProcessingState> ProcessTransactionAsync(ITransaction transaction, CancellationToken cancellation);

        ITransaction GetTransaction(long id);

        ITransaction CreateTransaction();

        IAsyncEnumerable<ITransaction> GetNonCommittedTransactionsAsync(CancellationToken cancellation = default);
    }

    public enum ProcessingState { Committed, Aborted };
}