# PowerSync.Common.SourceGenerators

Source generators for [PowerSync.Common](https://www.nuget.org/packages/PowerSync.Common/).

## ⚠️ Project Status

This package is in alpha and is not yet considered ready for production use. See our feature status definitions [here](https://docs.powersync.com/resources/feature-status).

## `[GenerateSqlMapper]`

Mark a `partial` class, struct or record with `[GenerateSqlMapper]` to generate its `IFromRow<T>` implementation, so it can be used with `GetAll<T>`, `Get<T>`, `GetOptional<T>` and `Watch<T>` without writing a mapper by hand.

```csharp
using PowerSync.Common.DB;
using PowerSync.Common.DB.Schema.Attributes;

[GenerateSqlMapper]
public partial class TodoItem
{
    [Column("id")]
    public string Id { get; set; } = "";

    [Column("description")]
    public string Description { get; set; } = "";

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    public bool Completed { get; set; }
}

[GenerateSqlMapper]
public partial record ListSummary(string Id, string Name, [property: Column("todo_count")] long TodoCount);

var todos = await db.GetAll<TodoItem>("SELECT * FROM todos");
var lists = await db.GetAll<ListSummary>(
    "SELECT lists.id, lists.name, count(todos.id) AS todo_count FROM lists LEFT JOIN todos ON todos.list_id = lists.id GROUP BY lists.id");
```

### Mapping rules

- Each settable property is read from the column named by its `[Column]` attribute, or otherwise the column with the property's name. Column names are matched case-insensitively, like SQLite identifiers.
- Constructor parameters are matched to properties of the same name (ignoring case) and use that property's column. A primary constructor, such as that of a positional record, is used if there is one. Otherwise the parameterless constructor is used, or failing that the constructor with the most parameters.
- Properties marked with `[Ignored]`, private properties, and properties without an accessible setter are skipped.
- Nullable members (`string?`, `int?`, ...) map SQL `NULL` to `null`. Non-nullable members throw if the column is `NULL`. A column missing from the result set always throws.
- Supported types are `string`, `bool`, all integer types, `float`, `double`, `decimal`, `DateTime`, `DateTimeOffset`, `Guid`, enums (stored as integers), `byte[]`, and their nullable forms. Any other type is a compile error: mark the property with `[Ignored]` or implement `IFromRow<T>` yourself.

The package must be used alongside `PowerSync.Common`, which provides `IFromRow<T>`, `[Column]` and `[Ignored]`.
