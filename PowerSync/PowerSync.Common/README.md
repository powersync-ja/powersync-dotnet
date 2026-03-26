# PowerSync SDK .NET Common

This package contains a .NET implementation of a PowerSync database connector and streaming sync bucket implementation.

## ⚠️ Project Status & Release Note

This package is in beta and is considered ready for production use for tested use cases. See our feature status definitions [here](https://docs.powersync.com/resources/feature-status).

## Installation

This package is published on [NuGet](https://www.nuget.org/packages/PowerSync.Common).

```bash
dotnet add package PowerSync.Common
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
            table.AddRow(line.id, line.name, line.owner_id, line.created_at);
        }
    }
}, cts.Token);
```
