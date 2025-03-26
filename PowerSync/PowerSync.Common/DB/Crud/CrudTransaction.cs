namespace PowerSync.Common.DB.Crud;

using System;
using System.Threading.Tasks;

public class CrudTransaction(CrudEntry[] crud, Func<string?, Task> complete, long? transactionId = null) : CrudBatch(crud, false, complete)
{
    public long? TransactionId { get; private set; } = transactionId;
}