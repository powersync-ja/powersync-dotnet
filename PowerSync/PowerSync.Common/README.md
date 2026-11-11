# PowerSync SDK .NET Common

This package contains a .NET implementation of a PowerSync database connector and streaming sync bucket implementation.

## Installation

This package is published on [NuGet](https://www.nuget.org/packages/PowerSync.Common).

```bash
dotnet add package PowerSync.Common
```

## Usage

### Simple Query

```csharp

private record ListResult(string Id, string Name, string OwnerId, string CreatedAt) : IFromRow<ListResult>
{
    public static ListResult FromRow(IQueryRow row) => new(
        row.GetString("id"),
        row.GetString("name"),
        row.GetString("owner_id"),
        row.GetString("created_at"));
}

static async Task Main() {

    var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
        Database = new SQLOpenOptions { DbFilename = "cli-example.db" },
        Schema = AppSchema.PowerSyncSchema,
    });
    await db.Init();

    // Map rows with a type implementing IFromRow<T>
    var lists = await db.GetAll<ListResult>("select * from lists");

    // Or map rows with a lambda
    var names = await db.GetAll("select name from lists", null, row => row.GetString("name"));

    // Or read the rows directly
    var result = await db.GetAll("select * from lists");
    foreach (var row in result.Rows)
    {
        Console.WriteLine(row.GetString("name"));
    }
}

```

Mappers (`IFromRow<T>.FromRow` and `SqlMapper<T>` lambdas) run while the query result is being read, and the `IQueryRow` they receive is only valid during that call. Copy the values you need out of it instead of storing the row; use `GetAll`/`Get` without a mapper to keep rows around.

### Watched queries

Watched queries will automatically update when a dependant table is updated.
Call `Watch()` synchronously to ensure the watcher is fully initialized before execution continues.

```csharp
var cts = new CancellationTokenSource();
var listener = db.Watch<ListResult>("select * from lists", null, new SQLWatchOptions { Signal = cts.Token });
_ = Task.Run(async () =>
{
    await foreach (var results in listener)
    {
        table.Rows.Clear();
        foreach (var line in results)
        {
            table.AddRow(line.Id, line.Name, line.OwnerId, line.CreatedAt);
        }
    }
}, cts.Token);
```
