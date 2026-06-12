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
