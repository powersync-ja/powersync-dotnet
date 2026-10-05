namespace PowerSync.Common.DB.Schema;

public abstract class BaseTable
{
    public abstract string Name { get; set; }
    public abstract void Validate();
}
