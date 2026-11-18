namespace PowerSync.Common.DB;

using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Column names of a result set, shared by all of its rows, with a case-insensitive name to ordinal lookup.
/// </summary>
internal sealed class ColumnMap
{
    private const int DictionaryThreshold = 16;

    private readonly string[] names;
    private readonly string[] lookupCache;
    private readonly Dictionary<string, int>? indices;

    public ColumnMap(string[] names)
    {
        this.names = names;
        lookupCache = (string[])names.Clone();
        if (names.Length > DictionaryThreshold)
        {
            indices = new Dictionary<string, int>(names.Length, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Length; i++)
            {
                indices.TryAdd(names[i], i);
            }
        }
    }

    public IReadOnlyList<string> Names => names;

    public int Count => names.Length;

    public int IndexOf(string columnName)
    {
        var cache = lookupCache;
        for (int i = 0; i < cache.Length; i++)
        {
            if (ReferenceEquals(cache[i], columnName))
            {
                return i;
            }
        }

        int index = Resolve(columnName);
        cache[index] = columnName;
        return index;
    }

    private int Resolve(string columnName)
    {
        if (columnName is null)
        {
            throw new ArgumentNullException(nameof(columnName));
        }

        if (indices != null)
        {
            if (indices.TryGetValue(columnName, out var index))
            {
                return index;
            }
        }
        else
        {
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        throw new ArgumentException($"Column '{columnName}' does not exist in the result set.", nameof(columnName));
    }
}

/// <summary>
/// Conversions from the values SQLite returns (<c>long</c>, <c>double</c>, <c>string</c>, <c>byte[]</c>), with
/// fast paths for the common storage types and <see cref="Convert"/> semantics for everything else.
/// </summary>
internal static class QueryValue
{
    public static string ToString(object value) => value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture)!;
    public static long ToInt64(object value) => value is long l ? l : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    public static int ToInt32(object value) => value is long l ? checked((int)l) : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    public static bool ToBoolean(object value) => value is long l ? l != 0 : Convert.ToBoolean(value, CultureInfo.InvariantCulture);

    public static double ToDouble(object value) => value switch
    {
        double d => d,
        long l => l,
        _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
    };

    public static DateTime ToDateTime(object value) => value is string s
        ? ParseDateTime(s)
        : Convert.ToDateTime(value, CultureInfo.InvariantCulture);

    public static DateTime ParseDateTime(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

/// <summary>
/// Implements the by-name <see cref="IQueryRow"/> getters on top of ordinal reads, which return false for NULL.
/// </summary>
internal abstract class QueryRowBase(ColumnMap columns) : IQueryRow
{
    protected readonly ColumnMap ColumnMap = columns;

    public IReadOnlyList<string> Columns => ColumnMap.Names;

    protected abstract object? ValueAt(int ordinal);
    protected abstract bool TryGetString(int ordinal, out string value);
    protected abstract bool TryGetInt32(int ordinal, out int value);
    protected abstract bool TryGetInt64(int ordinal, out long value);
    protected abstract bool TryGetBoolean(int ordinal, out bool value);
    protected abstract bool TryGetDouble(int ordinal, out double value);
    protected abstract bool TryGetDateTime(int ordinal, out DateTime value);

    protected virtual int Ordinal(string columnName) => ColumnMap.IndexOf(columnName);

    private static InvalidOperationException NullColumn(string columnName) =>
        new($"Column '{columnName}' is null.");

    public object? GetValue(string columnName) => ValueAt(Ordinal(columnName));

    public string GetString(string columnName) => TryGetString(Ordinal(columnName), out var value) ? value : throw NullColumn(columnName);
    public int GetInt(string columnName) => TryGetInt32(Ordinal(columnName), out var value) ? value : throw NullColumn(columnName);
    public long GetLong(string columnName) => TryGetInt64(Ordinal(columnName), out var value) ? value : throw NullColumn(columnName);
    public bool GetBool(string columnName) => TryGetBoolean(Ordinal(columnName), out var value) ? value : throw NullColumn(columnName);
    public double GetDouble(string columnName) => TryGetDouble(Ordinal(columnName), out var value) ? value : throw NullColumn(columnName);
    public DateTime GetDateTime(string columnName) => TryGetDateTime(Ordinal(columnName), out var value) ? value : throw NullColumn(columnName);

    public string? GetStringOrNull(string columnName) => TryGetString(Ordinal(columnName), out var value) ? value : null;
    public int? GetIntOrNull(string columnName) => TryGetInt32(Ordinal(columnName), out var value) ? value : null;
    public long? GetLongOrNull(string columnName) => TryGetInt64(Ordinal(columnName), out var value) ? value : null;
    public bool? GetBoolOrNull(string columnName) => TryGetBoolean(Ordinal(columnName), out var value) ? value : null;
    public double? GetDoubleOrNull(string columnName) => TryGetDouble(Ordinal(columnName), out var value) ? value : null;
    public DateTime? GetDateTimeOrNull(string columnName) => TryGetDateTime(Ordinal(columnName), out var value) ? value : null;
}

/// <summary>
/// A row whose values have been copied out of the reader, so it stays valid after the query completes.
/// </summary>
internal sealed class QueryRow(ColumnMap columns, object?[] values) : QueryRowBase(columns)
{
    protected override object? ValueAt(int ordinal) => values[ordinal];

    protected override bool TryGetString(int ordinal, out string value)
    {
        if (values[ordinal] is not { } raw)
        {
            value = null!;
            return false;
        }
        value = QueryValue.ToString(raw);
        return true;
    }

    protected override bool TryGetInt32(int ordinal, out int value)
    {
        if (values[ordinal] is not { } raw)
        {
            value = default;
            return false;
        }
        value = QueryValue.ToInt32(raw);
        return true;
    }

    protected override bool TryGetInt64(int ordinal, out long value)
    {
        if (values[ordinal] is not { } raw)
        {
            value = default;
            return false;
        }
        value = QueryValue.ToInt64(raw);
        return true;
    }

    protected override bool TryGetBoolean(int ordinal, out bool value)
    {
        if (values[ordinal] is not { } raw)
        {
            value = default;
            return false;
        }
        value = QueryValue.ToBoolean(raw);
        return true;
    }

    protected override bool TryGetDouble(int ordinal, out double value)
    {
        if (values[ordinal] is not { } raw)
        {
            value = default;
            return false;
        }
        value = QueryValue.ToDouble(raw);
        return true;
    }

    protected override bool TryGetDateTime(int ordinal, out DateTime value)
    {
        if (values[ordinal] is not { } raw)
        {
            value = default;
            return false;
        }
        value = QueryValue.ToDateTime(raw);
        return true;
    }
}
