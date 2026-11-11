namespace PowerSync.Common.MDSQLite;

using Microsoft.Data.Sqlite;

using PowerSync.Common.DB;

using SQLitePCL;

/// <summary>
/// An <see cref="IQueryRow"/> that reads the current row of a live <see cref="SqliteDataReader"/> without copying it.
/// One instance is reused for every row of a result set, and it only stays valid while the reader is open.
/// </summary>
internal sealed class SqliteReaderRow : QueryRowBase
{
    private readonly SqliteDataReader reader;
    private readonly sqlite3_stmt stmt;
    private bool released;

    private SqliteReaderRow(ColumnMap columns, SqliteDataReader reader, sqlite3_stmt stmt) : base(columns)
    {
        this.reader = reader;
        this.stmt = stmt;
    }

    /// <summary>
    /// Creates a row over <paramref name="reader"/>, which must already be positioned on its first row.
    /// </summary>
    public static SqliteReaderRow Create(SqliteDataReader reader) =>
        new(ReadColumns(reader), reader, reader.Handle!);

    public static ColumnMap ReadColumns(SqliteDataReader reader)
    {
        var names = new string[reader.FieldCount];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = reader.GetName(i);
        }
        return new ColumnMap(names);
    }

    /// <summary>
    /// Copies the current row out of <paramref name="reader"/>.
    /// </summary>
    public static QueryRow Materialize(SqliteDataReader reader, ColumnMap columns) =>
        new(columns, ReadValues(reader, reader.Handle!, columns.Count));

    private static object?[] ReadValues(SqliteDataReader reader, sqlite3_stmt stmt, int count)
    {
        var values = new object?[count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = raw.sqlite3_column_type(stmt, i) switch
            {
                raw.SQLITE_NULL => null,
                raw.SQLITE_INTEGER => raw.sqlite3_column_int64(stmt, i),
                raw.SQLITE_FLOAT => raw.sqlite3_column_double(stmt, i),
                raw.SQLITE_TEXT => raw.sqlite3_column_text(stmt, i).utf8_to_string(),
                _ => reader.GetValue(i),
            };
        }
        return values;
    }

    /// <summary>
    /// Maps the current row. A mapper that returns the row itself gets a materialized copy instead.
    /// </summary>
    public T Map<T>(SqlMapper<T> mapper)
    {
        var result = mapper(this);
        if (!typeof(T).IsValueType && ReferenceEquals(result, this))
        {
            return (T)(object)Materialize(reader, ColumnMap);
        }
        return result;
    }

    /// <summary>
    /// Marks the row as no longer readable, so a row that escaped its mapper fails instead of reading a closed reader.
    /// </summary>
    public void Release() => released = true;

    protected override int Ordinal(string columnName)
    {
        if (released)
        {
            throw new InvalidOperationException(
                "This row is only valid inside the mapper it was passed to. Copy the values you need, or use the untyped GetAll/Get overloads to keep rows.");
        }
        return ColumnMap.IndexOf(columnName);
    }

    private int TypeAt(int ordinal) => raw.sqlite3_column_type(stmt, ordinal);

    private object Boxed(int ordinal) => reader.GetValue(ordinal);

    protected override object? ValueAt(int ordinal) =>
        TypeAt(ordinal) == raw.SQLITE_NULL ? null : reader.GetValue(ordinal);

    protected override bool TryGetString(int ordinal, out string value)
    {
        switch (TypeAt(ordinal))
        {
            case raw.SQLITE_NULL:
                value = null!;
                return false;
            case raw.SQLITE_TEXT:
                value = raw.sqlite3_column_text(stmt, ordinal).utf8_to_string();
                return true;
            default:
                value = QueryValue.ToString(Boxed(ordinal));
                return true;
        }
    }

    protected override bool TryGetInt32(int ordinal, out int value)
    {
        switch (TypeAt(ordinal))
        {
            case raw.SQLITE_NULL:
                value = default;
                return false;
            case raw.SQLITE_INTEGER:
                value = checked((int)raw.sqlite3_column_int64(stmt, ordinal));
                return true;
            default:
                value = QueryValue.ToInt32(Boxed(ordinal));
                return true;
        }
    }

    protected override bool TryGetInt64(int ordinal, out long value)
    {
        switch (TypeAt(ordinal))
        {
            case raw.SQLITE_NULL:
                value = default;
                return false;
            case raw.SQLITE_INTEGER:
                value = raw.sqlite3_column_int64(stmt, ordinal);
                return true;
            default:
                value = QueryValue.ToInt64(Boxed(ordinal));
                return true;
        }
    }

    protected override bool TryGetBoolean(int ordinal, out bool value)
    {
        switch (TypeAt(ordinal))
        {
            case raw.SQLITE_NULL:
                value = default;
                return false;
            case raw.SQLITE_INTEGER:
                value = raw.sqlite3_column_int64(stmt, ordinal) != 0;
                return true;
            default:
                value = QueryValue.ToBoolean(Boxed(ordinal));
                return true;
        }
    }

    protected override bool TryGetDouble(int ordinal, out double value)
    {
        switch (TypeAt(ordinal))
        {
            case raw.SQLITE_NULL:
                value = default;
                return false;
            case raw.SQLITE_FLOAT:
                value = raw.sqlite3_column_double(stmt, ordinal);
                return true;
            case raw.SQLITE_INTEGER:
                value = raw.sqlite3_column_int64(stmt, ordinal);
                return true;
            default:
                value = QueryValue.ToDouble(Boxed(ordinal));
                return true;
        }
    }

    protected override bool TryGetDateTime(int ordinal, out DateTime value)
    {
        switch (TypeAt(ordinal))
        {
            case raw.SQLITE_NULL:
                value = default;
                return false;
            case raw.SQLITE_TEXT:
                value = QueryValue.ParseDateTime(raw.sqlite3_column_text(stmt, ordinal).utf8_to_string());
                return true;
            default:
                value = QueryValue.ToDateTime(Boxed(ordinal));
                return true;
        }
    }
}
