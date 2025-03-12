namespace PowerSync.Common.DB.Crud;

using System;
using System.Threading.Tasks;

public class CrudBatch(CrudEntry[] Crud, bool HaveMore, Func<string?, Task> CompleteCallback)
{
    public CrudEntry[] Crud { get; private set; } = Crud;

    public bool HaveMore { get; private set; } = HaveMore;

    public async Task Complete(string? checkpoint = null)
    {
        await CompleteCallback(checkpoint);
    }
}