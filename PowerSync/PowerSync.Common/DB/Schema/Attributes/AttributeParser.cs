namespace PowerSync.Common.DB.Schema.Attributes;

using System.Reflection;

internal class AttributeParser
{
    private readonly Type _type;
    private readonly TableAttribute _tableAttr;
    private readonly Dictionary<string, ColumnType> _columns;
    private readonly Dictionary<string, List<string>> _indexes;
    private readonly TableOptions _options;

    public AttributeParser(Type type)
    {
        _type = type;

        _tableAttr = _type.GetCustomAttribute<TableAttribute>();
        if (_tableAttr == null)
        {
            throw new CustomAttributeFormatException("Table classes must be marked with TableAttribute.");
        }

        _columns = GetColumns();
        _indexes = GetIndexes();
        _options = GetTableOptions();
    }

    public Table GetTable()
    {
        return new Table(
            _tableAttr.Name,
            _columns,
            _options
        );
    }

    private Dictionary<string, ColumnType> GetColumns()
    {
        var columns = new Dictionary<string, ColumnType>();
        PropertyInfo? idProperty = null;

        foreach (var prop in _type.GetProperties())
        {
            // TODO: Allow setting name via ColumnAttribute?
            var name = prop.Name;

            // Handle 'id' field separately
            if (name.ToLowerInvariant() == "id")
            {
                idProperty = prop;
                continue;
            }


            var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
            var columnType = columnAttr != null
                ? columnAttr.ColumnType
                : PropertyTypeToColumnType(prop.PropertyType);
            columns.Add(name, columnType);
        }

        // Validate 'id' property
        if (idProperty == null)
        {
            throw new InvalidOperationException("A public string 'id' property is required.");
        }
        if (idProperty.PropertyType != typeof(string))
        {
            throw new InvalidOperationException("Property 'id' must be of type string.");
        }

        return columns;
    }

    private Dictionary<string, List<string>> GetIndexes()
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
        var innerType = Nullable.GetUnderlyingType(propertyType);

        return propertyType switch
        {
            // TEXT types
            _ when propertyType == typeof(string) => ColumnType.Text,
            _ when propertyType == typeof(char) => ColumnType.Text,
            _ when propertyType == typeof(Guid) => ColumnType.Text,
            _ when propertyType == typeof(DateTime) => ColumnType.Text,
            _ when propertyType == typeof(DateTimeOffset) => ColumnType.Text,

            // INTEGER types
            _ when propertyType == typeof(Enum) => ColumnType.Integer,
            _ when propertyType == typeof(bool) => ColumnType.Integer,   // bool
            _ when propertyType == typeof(sbyte) => ColumnType.Integer,  // i8
            _ when propertyType == typeof(byte) => ColumnType.Integer,   // u8
            _ when propertyType == typeof(short) => ColumnType.Integer,  // i16
            _ when propertyType == typeof(ushort) => ColumnType.Integer, // u16
            _ when propertyType == typeof(int) => ColumnType.Integer,    // i32
            _ when propertyType == typeof(uint) => ColumnType.Integer,   // u32
            _ when propertyType == typeof(long) => ColumnType.Integer,   // i64
            _ when propertyType == typeof(ulong) => ColumnType.Integer,  // u64
            // .NET 5.0+, but we need to support .NET Standard 2.0
            // _ when propertyType == typeof(nint) => ColumnType.Integer,   // isize
            // _ when propertyType == typeof(nuint) => ColumnType.Integer,  // usize

            // REAL types
            _ when propertyType == typeof(float) => ColumnType.Real,
            _ when propertyType == typeof(double) => ColumnType.Real,
            _ when propertyType == typeof(decimal) => ColumnType.Real,

            // Fallback
            _ => ColumnType.Text
        };
    }

    private TableOptions GetTableOptions()
    {
        return new TableOptions(
            indexes: _indexes,
            localOnly: _tableAttr.LocalOnly,
            insertOnly: _tableAttr.InsertOnly,
            viewName: _tableAttr.ViewName,
            trackMetadata: _tableAttr.TrackMetadata,
            trackPreviousValues: GetTrackPreviousOptions(),
            ignoreEmptyUpdates: _tableAttr.IgnoreEmptyUpdates
        );
    }

    private TrackPreviousOptions? GetTrackPreviousOptions()
    {
        TrackPrevious trackPrevious = _tableAttr.TrackPreviousValues;
        if (trackPrevious == TrackPrevious.None)
        {
            return null;
        }

        if (trackPrevious.HasFlag(TrackPrevious.Columns) && trackPrevious.HasFlag(TrackPrevious.Table))
        {
            throw new CustomAttributeFormatException("Cannot specify both TrackPrevious.Columns and TrackPrevious.Table on the same table.");
        }

        if (!trackPrevious.HasFlag(TrackPrevious.Columns)
            && !trackPrevious.HasFlag(TrackPrevious.Table)
            && trackPrevious.HasFlag(TrackPrevious.OnlyWhenChanged))
        {
            throw new CustomAttributeFormatException("Cannot specify TrackPrevious.OnlyWhenChanged without also specifying either TrackPrevious.Columns or TrackPrevious.Table.");
        }

        bool trackWholeTable = _tableAttr.TrackPreviousValues.HasFlag(TrackPrevious.Table);
        bool onlyWhenChanged = trackPrevious.HasFlag(TrackPrevious.OnlyWhenChanged);

        return new TrackPreviousOptions
        {
            Columns = trackWholeTable ? null : GetTrackedColumns(),
            OnlyWhenChanged = onlyWhenChanged,
        };
    }

    private List<string> GetTrackedColumns()
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
