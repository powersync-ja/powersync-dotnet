namespace PowerSync.Common.DB;

using System.Collections.Generic;
using System.Globalization;

internal sealed class QueryRow(IReadOnlyList<string> columns, IReadOnlyDictionary<string, int> columnIndices, object?[] values) : IQueryRow
{
    public IReadOnlyList<string> Columns { get; } = columns;

    public object? GetValue(string columnName)
    {
        if (!columnIndices.TryGetValue(columnName, out var index))
        {
            throw new ArgumentException($"Column '{columnName}' does not exist in the result set.", nameof(columnName));
        }
        return values[index];
    }

    private object GetRequired(string columnName)
    {
        return GetValue(columnName) ?? throw new InvalidOperationException($"Column '{columnName}' is null.");
    }

    public string GetString(string columnName) => ToString(GetRequired(columnName));
    public int GetInt(string columnName) => Convert.ToInt32(GetRequired(columnName), CultureInfo.InvariantCulture);
    public long GetLong(string columnName) => Convert.ToInt64(GetRequired(columnName), CultureInfo.InvariantCulture);
    public bool GetBool(string columnName) => Convert.ToBoolean(GetRequired(columnName), CultureInfo.InvariantCulture);
    public double GetDouble(string columnName) => Convert.ToDouble(GetRequired(columnName), CultureInfo.InvariantCulture);
    public DateTime GetDateTime(string columnName) => ToDateTime(GetRequired(columnName));

    public string? GetStringOrNull(string columnName) => GetValue(columnName) is { } value ? ToString(value) : null;
    public int? GetIntOrNull(string columnName) => GetValue(columnName) is { } value ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : null;
    public long? GetLongOrNull(string columnName) => GetValue(columnName) is { } value ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : null;
    public bool? GetBoolOrNull(string columnName) => GetValue(columnName) is { } value ? Convert.ToBoolean(value, CultureInfo.InvariantCulture) : null;
    public double? GetDoubleOrNull(string columnName) => GetValue(columnName) is { } value ? Convert.ToDouble(value, CultureInfo.InvariantCulture) : null;
    public DateTime? GetDateTimeOrNull(string columnName) => GetValue(columnName) is { } value ? ToDateTime(value) : null;

    private static string ToString(object value) => Convert.ToString(value, CultureInfo.InvariantCulture)!;

    private static DateTime ToDateTime(object value) => value is string s
        ? DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        : Convert.ToDateTime(value, CultureInfo.InvariantCulture);
}
