namespace Common.DB.Crud;

using System;
using System.Threading.Tasks;

public class CrudBatch(CrudEntry[] Crud, bool HaveMore, Func<string?, Task> Complete)
{
    public CrudEntry[] Crud { get; private set; } = Crud;

    public bool HaveMore { get; private set; } = HaveMore;

    public Func<string?, Task> Complete { get; private set; } = Complete;
}