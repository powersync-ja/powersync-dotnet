namespace PowerSync.Common.DB.Schema.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class IndexAttribute : Attribute
{
    public string Name { get; }
    public string[] Columns { get; }

    public IndexAttribute(string name, string[] columns)
    {
        Name = name;
        Columns = columns;
    }
}

