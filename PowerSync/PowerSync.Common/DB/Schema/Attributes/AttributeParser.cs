namespace PowerSync.Common.DB.Schema.Attributes;

using System.Reflection;

using Dapper;

internal class AttributeParser
{
    private readonly Type _type;
    private readonly TableAttribute _tableAttr;

    public string TableName
    {
        get { return _tableAttr.Name; }
    }

    public AttributeParser(Type type)
    {
        _type = type;

        _tableAttr = _type.GetCustomAttribute<TableAttribute>();
        if (_tableAttr == null)
        {
            throw new InvalidOperationException("Table classes must be marked with TableAttribute.");
        }
    }

    public Table ParseTable()
    {
        return new Table(
            name: _tableAttr.Name,
            columns: ParseColumns(),
            options: ParseTableOptions()
        );
    }

    public Dictionary<string, ColumnType> ParseColumns()
    {
        var columns = new Dictionary<string, ColumnType>();
        PropertyInfo? idProperty = null;

        foreach (var prop in _type.GetProperties())
        {
            if (prop.GetCustomAttribute<IgnoredAttribute>() != null) continue;

            var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
            var columnName = columnAttr?.Name ?? prop.Name;

            // Handle 'id' field separately
            if (columnName.ToLowerInvariant() == "id")
            {
                if (idProperty != null)
                {
                    throw new InvalidOperationException($"Cannot define multiple ID columns for table '{_tableAttr.Name}'.");
                }
                idProperty = prop;
                continue;
            }

            var userColumnType = columnAttr?.ColumnType ?? ColumnType.Inferred;

            // Infer column type from property's type
            var columnType = userColumnType == ColumnType.Inferred
                ? PropertyTypeToColumnType(prop.PropertyType)
                : userColumnType;
            columns.Add(columnName, columnType);
        }

        // Validate 'id' property exists and is a string
        if (idProperty == null)
        {
            throw new InvalidOperationException($"An 'id' property is required for table '{_tableAttr.Name}'.");
        }
        if (idProperty.PropertyType != typeof(string))
        {
            throw new InvalidOperationException($"ID Property '{idProperty.Name}' must be of type string.");
        }
        var idAttr = idProperty.GetCustomAttribute<ColumnAttribute>();
        if (idAttr != null)
        {
            // ID column only supports Text and Inferred as options
            if (idAttr.ColumnType != ColumnType.Text && idAttr.ColumnType != ColumnType.Inferred)
            {
                throw new InvalidOperationException
                (
                    $"ID Property '{idProperty.Name}' must have ColumnType set to ColumnType.Text or ColumnType.Inferred."
                );
            }
        }

        return columns;
    }

    public Dictionary<string, List<string>> ParseIndexes()
    {
        var indexes = new Dictionary<string, List<string>>();
        var indexAttrs = _type.GetCustomAttributes<IndexAttribute>();
        foreach (var index in indexAttrs)
        {
            var name = index.Name;
            var columns = index.Columns.ToList();
            indexes.Add(name, columns);
        }
        return indexes;
    }

    private ColumnType PropertyTypeToColumnType(Type propertyType)
    {
        Type underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return underlyingType switch
        {
            // TEXT types
            Type t when t == typeof(string) => ColumnType.Text,
            Type t when t == typeof(char) => ColumnType.Text,
            Type t when t == typeof(Guid) => ColumnType.Text,
            Type t when t == typeof(DateTime) => ColumnType.Text,
            Type t when t == typeof(DateTimeOffset) => ColumnType.Text,
            Type t when t == typeof(TimeSpan) => ColumnType.Text,
            // 'decimal' is 128-bit, ColumnType.Real is only 64-bit
            Type t when t == typeof(decimal) => ColumnType.Text,

            // INTEGER types
            Type t when t.IsEnum => ColumnType.Integer,
            Type t when t == typeof(bool) => ColumnType.Integer,   // bool
            Type t when t == typeof(sbyte) => ColumnType.Integer,  // i8
            Type t when t == typeof(byte) => ColumnType.Integer,   // u8
            Type t when t == typeof(short) => ColumnType.Integer,  // i16
            Type t when t == typeof(ushort) => ColumnType.Integer, // u16
            Type t when t == typeof(int) => ColumnType.Integer,    // i32
            Type t when t == typeof(uint) => ColumnType.Integer,   // u32
            Type t when t == typeof(long) => ColumnType.Integer,   // i64
            Type t when t == typeof(ulong) => ColumnType.Integer,  // u64
            // .NET 5.0+ only
            // Type t when t == typeof(nint) => ColumnType.Integer,   // isize
            // Type t when t == typeof(nuint) => ColumnType.Integer,  // usize

            // REAL types
            Type t when t == typeof(float) => ColumnType.Real,
            Type t when t == typeof(double) => ColumnType.Real,

            // Fallback
            _ => throw new InvalidOperationException($"Unable to automatically infer ColumnType of property type '{underlyingType.Name}'."),
        };
    }

    public TableOptions ParseTableOptions()
    {
        return new TableOptions(
            indexes: ParseIndexes(),
            localOnly: _tableAttr.LocalOnly,
            insertOnly: _tableAttr.InsertOnly,
            viewName: _tableAttr.ViewName,
            trackMetadata: _tableAttr.TrackMetadata,
            trackPreviousValues: ParseTrackPreviousOptions(),
            ignoreEmptyUpdates: _tableAttr.IgnoreEmptyUpdates
        );
    }

    public TrackPreviousOptions? ParseTrackPreviousOptions()
    {
        TrackPrevious trackPrevious = _tableAttr.TrackPreviousValues;
        if (trackPrevious == TrackPrevious.None)
        {
            return null;
        }

        if (trackPrevious.HasFlag(TrackPrevious.Columns) && trackPrevious.HasFlag(TrackPrevious.Table))
        {
            throw new InvalidOperationException("Cannot specify both TrackPrevious.Columns and TrackPrevious.Table on the same table.");
        }

        if (!trackPrevious.HasFlag(TrackPrevious.Columns)
            && !trackPrevious.HasFlag(TrackPrevious.Table)
            && trackPrevious.HasFlag(TrackPrevious.OnlyWhenChanged))
        {
            throw new InvalidOperationException("Cannot specify TrackPrevious.OnlyWhenChanged without also specifying either TrackPrevious.Columns or TrackPrevious.Table.");
        }

        bool trackWholeTable = _tableAttr.TrackPreviousValues.HasFlag(TrackPrevious.Table);
        bool onlyWhenChanged = trackPrevious.HasFlag(TrackPrevious.OnlyWhenChanged);

        return new TrackPreviousOptions
        {
            Columns = trackWholeTable ? null : ParseTrackedColumns(),
            OnlyWhenChanged = onlyWhenChanged,
        };
    }

    public CustomPropertyTypeMap ParseDapperTypeMap()
    {
        return new(
            _type,
            (type, columnName) => type.GetProperties()
                .FirstOrDefault(prop => prop.GetCustomAttributes()
                    .OfType<ColumnAttribute>()
                    .Any(columnAttr => columnAttr.Name == columnName))
        );
    }

    public void RegisterDapperTypeMap()
    {
        // Only register type map if some Column("custom_name") is found
        if (_type
                .GetProperties()
                .Any(prop => prop
                    .GetCustomAttributes()
                    .OfType<ColumnAttribute>()
                    .Any(attr => attr.Name != null)))
        {
            Dapper.SqlMapper.SetTypeMap(_type, ParseDapperTypeMap());
        }
    }

    public List<string> ParseTrackedColumns()
    {
        var trackedColumns = new List<string>();
        foreach (var prop in _type.GetProperties())
        {
            var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
            if (columnAttr == null || !columnAttr.TrackPrevious) continue;

            trackedColumns.Add(prop.Name);
        }
        return trackedColumns;
    }
}
