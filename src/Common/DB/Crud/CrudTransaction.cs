namespace Common.DB.Crud;

using System;
using System.Threading.Tasks;

public class CrudTransaction(
    CrudEntry[] crud,
    Func<string?, Task> complete, int? transactionId = null) : CrudBatch(crud, false, complete)
{
    public int? TransactionId { get; private set; } = transactionId;
}