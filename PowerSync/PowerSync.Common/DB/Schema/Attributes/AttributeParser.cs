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
            throw new CustomAttributeFormatException("Table classes must be marked with TableAttribute.");
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
            var columnAttr = prop.GetCustomAttribute<ColumnAttribute>();
            var columnName = columnAttr?.Name ?? prop.Name;

            // Handle 'id' field separately
            // TODO prevent defining multiple id columns (eg. 'id', 'Id', 'ID')
            if (columnName.ToLowerInvariant() == "id")
            {
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
            throw new InvalidOperationException("A public string 'id' property is required.");
        }
        if (idProperty.PropertyType != typeof(string))
        {
            throw new InvalidOperationException($"Property '{idProperty.Name}' must be of type string.");
        }
        var idAttr = idProperty.GetCustomAttribute<ColumnAttribute>();
        if (idAttr != null)
        {
            // ID column only supports Text and Inferred as options
            if (idAttr.ColumnType != ColumnType.Text && idAttr.ColumnType != ColumnType.Inferred)
            {
                throw new InvalidOperationException
                (
                    $"Property '{idProperty.Name}' must have ColumnType set to either ColumnType.Text or ColumnType.Inferred."
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
        var innerType = Nullable.GetUnderlyingType(propertyType);

        return propertyType switch
        {
            // TEXT types
            _ when propertyType == typeof(string) => ColumnType.Text,
            _ when propertyType == typeof(char) => ColumnType.Text,
            _ when propertyType == typeof(Guid) => ColumnType.Text,
            _ when propertyType == typeof(DateTime) => ColumnType.Text,
            _ when propertyType == typeof(DateTimeOffset) => ColumnType.Text,
            _ when propertyType == typeof(TimeSpan) => ColumnType.Text,
            // 'decimal' is 128-bit, ColumnType.Real is only 64-bit
            _ when propertyType == typeof(decimal) => ColumnType.Text,

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
            // .NET 5.0+ only
            // _ when propertyType == typeof(nint) => ColumnType.Integer,   // isize
            // _ when propertyType == typeof(nuint) => ColumnType.Integer,  // usize

            // REAL types
            _ when propertyType == typeof(float) => ColumnType.Real,
            _ when propertyType == typeof(double) => ColumnType.Real,

            // Fallback
            // TODO: Maybe raise a console warning / throw an error if unable to infer type?
            _ => ColumnType.Text
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
