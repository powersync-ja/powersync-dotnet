namespace PowerSync.Common.DB.Schema;

public enum ColumnType
{
    Text,
    Integer,
    Real,
    /// <summary>
    /// Infers the column type based on the associated property's PropertyType.
    /// <b>NB:</b> `ColumnType.Inferred` can only be used when using the <see cref="Attributes" /> syntax.
    /// </summary>
    Inferred
}
