namespace Common.DB.Crud;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CrudTransaction(
    List<CrudEntry> crud,
    Func<string?, Task> complete,
    int? transactionId = null
    ) : CrudBatch(crud, false, complete)
{
    public int? TransactionId { get; private set; } = transactionId;
}