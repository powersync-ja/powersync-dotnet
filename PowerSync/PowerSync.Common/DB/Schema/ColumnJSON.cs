namespace PowerSync.Common.DB.Schema;

public enum ColumnType
{
    Text,
    Integer,
    Real,
    /// <summary>
    /// <para>Infers the column type based on the associated property's PropertyType.</para>
    /// <para>**NB:** `ColumnType.Inferred` can only be used when using the schema attributes syntax.</para>
    /// </summary>
    Inferred
}

class ColumnJSONOptions(string Name, ColumnType? Type)
{
    public string Name { get; set; } = Name;
    public ColumnType? Type { get; set; } = Type;
}

class ColumnJSON(ColumnJSONOptions options)
{
    public string Name { get; set; } = options.Name;

    public ColumnType Type { get; set; } = options.Type ?? ColumnType.Text;

    public object ToJSONObject()
    {
        if (Type == ColumnType.Inferred) throw new InvalidOperationException("Attempted to serialise Inferred column. ColumnType.Inferred is only valid as an argument to ColumnAttribute.");

        return new
        {
            name = Name,
            type = Type.ToString()
        };
    }
}
