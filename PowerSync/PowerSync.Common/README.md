# PowerSync SDK .NET Common 

This package contains a .NET implementation of a PowerSync database connector and streaming sync bucket implementation.

## ⚠️ Project Status & Release Note

This package is currently in an alpha state, intended strictly for testing. Expect breaking changes and instability as development continues.

Do not rely on this package for production use.

## Installation

This package is published on [NuGet](https://www.nuget.org/packages/PowerSync.Common).

```bash
dotnet add package PowerSync.Common --prerelease
```

## Usage 

### Simple Query

```csharp

private record ListResult(string id, string name, string owner_id, string created_at);

static async Task Main() {

    var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
        Database = new SQLOpenOptions { DbFilename = "cli-example.db" },
        Schema = AppSchema.PowerSyncSchema,
    });
    await db.Init();

    var lists = await db.GetAll<ListResult>("select * from lists");
}

```

### Watched queries
Watched queries will automatically update when a dependant table is updated. 
Awaiting `Watch()` ensures the watcher is fully initialized and ready to monitor database changes.

```csharp
await db.Watch("select * from lists", null, new WatchHandler<ListResult>
{
    OnResult = (results) =>
    {
        table.Rows.Clear();
        foreach (var line in results)
        {
            table.AddRow(line.id, line.name, line.owner_id, line.created_at);
        }
    },
    OnError = (error) =>
    {
        Console.WriteLine("Error: " + error.Message);
    }
});

```