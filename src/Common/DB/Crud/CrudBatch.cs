namespace Common.DB.Crud;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CrudBatch(List<CrudEntry> crud, bool haveMore, Func<string?, Task> complete)
{
    public List<CrudEntry> Crud { get; private set; } = crud ?? throw new ArgumentNullException(nameof(crud));

    public bool HaveMore { get; private set; } = haveMore;

    public Func<string?, Task> Complete { get; private set; } = complete ?? throw new ArgumentNullException(nameof(complete));
}