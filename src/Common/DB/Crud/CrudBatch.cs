namespace Common.DB.Crud;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CrudBatch(List<CrudEntry> Crud, bool HaveMore, Func<string?, Task> Complete)
{
    public List<CrudEntry> Crud { get; private set; } = Crud;

    public bool HaveMore { get; private set; } = HaveMore;

    public Func<string?, Task> Complete { get; private set; } = Complete;
}