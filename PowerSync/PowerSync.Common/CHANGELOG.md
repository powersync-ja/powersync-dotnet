# PowerSync.Common Changelog

## 0.0.11-alpha.1

- `MDSQLiteConnection` now runs query operations on another thread, which stops the caller thread from blocking.
- Removed the `RunListener` and `RunListenerAsync` APIs from `IEventStream`. Users are encouraged to use the `Listen` or `ListenAsync` APIs instead (`RunListener` itself was implemented using the `Listen` API).
- Changed the `PowerSyncDatabase.Watch` syntax to return an IAsyncEnumerable instead of accepting a callback. This allows users to handle database changes when they are ready instead of us eagerly running the callback as soon as a change is detected.

```csharp
// Optional cancellation token
var cts = new CancellationTokenSource();

// Register listener synchronously on the calling thread...
var listener = db.Watch<Todo>(
    "SELECT * FROM todos",
    [],
    new SQLWatchOptions { Signal = cts.Token }
);

// ...then listen to changes on another thread
_ = Task.Run(async () =>
{
    await foreach (var result in listener)
    {
        Console.WriteLine($"Number of todos: {result.Length}");
    }
}, cts.Token);

// Stop watching by cancelling token
cts.Cancel();
```

## 0.0.10-alpha.1

- Fixed watched queries sometimes resolving to the wrong underlying tables after a schema change.
- Fixed some properties in Table not being public when they are meant to be.
- Fixed a bug where custom indexes were not being sent to the PowerSync SQLite extension.
- Added a new model-based syntax for defining the PowerSync schema (the old syntax is still functional). This syntax uses classes marked with attributes to define the PowerSync schema. The classes can then also be used for queries later on.

```csharp
using PowerSync.Common.DB.Schema;
using PowerSync.Common.DB.Schema.Attributes;

[
    Table("todos"),
    Index("list", ["list_id"])
]
public class Todo
{
    [Column("id")]
    public string TodoId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("name")]
    public string Name { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("completed")]
    public bool Completed { get; set; }
}

public class Schema
{
    public static Schema AppSchema = new Schema(typeof(Todo));
}

// Usage
var todos = powerSync.GetAll<Todo>("SELECT * FROM todos");
```

## 0.0.9-alpha.1

- _Breaking:_ Further updated schema definition syntax.
  - Renamed `Schema` and `Table` to `CompiledSchema` and `CompiledTable` and renamed `SchemaFactory` and `TableFactory` to `Schema` and `Table`.
  - Made `CompiledSchema` and `CompiledTable` internal classes.
  - These are the last breaking changes to schema definition before entering beta.

```csharp
public static Table Assets = new Table
{
    Name = "assets",
    Columns =
    {
        ["make"] = ColumnType.Text,
        ["model"] = ColumnType.Text,
        // ...
    },
    Indexes =
    {
        ["makemodel"] = ["make", "model"],
    },
};

public static Table Customers = new Table
{
    Name = "customers",
    Columns =
    {
        ["name"] = ColumnType.Text,
    },
};

public static Schema PowerSyncSchema = new Schema(Assets, Customers);
```

## 0.0.8-alpha.1

- Updated the syntax for defining the app schema to use a factory pattern.
- Add support for [sync streams](https://docs.powersync.com/sync/streams/overview).
- Return an `IDisposable` from `PowerSync.Watch`, allowing for easier cancellation of watched queries.
- Replaced the old JSON-based method of extracting type information from queries with using Dapper internally for queries, improving memory usage and execution time for querying.
- Added non-generic overloads for `GetAll()`, `GetOptional()`, `Get()`, `Watch()` which return `dynamic`:

```csharp
dynamic asset = db.Get("SELECT id, description, make FROM assets");
Console.WriteLine($"Asset ID: {asset.id}");
```

## 0.0.7-alpha.1

- Added fallback to check the application's root directory for the PowerSync extension - fixing compatibility with WPF/WAP, .NET Framework <= 4.8, and other platforms that flatten DLLs into the base folder.
- Added `ExecuteBatch()` implementation.
- Added `GetUploadQueueStats()` to `PowerSyncDatabase`.
- Altered query methods, `Execute()`, `GetAll()`, `GetOptional()`, `Get()`, `Watch()`, to support null parameters in their parameters list, for example:

```csharp
db.Execute(
    "INSERT INTO assets(id, description, make) VALUES(?, ?, ?)",
    [id, name, null] // last parameter is an explicit null value
);
```

## 0.0.6-alpha.1

- Updated to the latest version (0.4.10) of the core extension.
- Dropping support for the legacy C# sync implementation.
- Add `trackPreviousValues` option on `TableOptions` which sets `CrudEntry.PreviousValues` to previous values on updates.
- Add `trackMetadata` option on `TableOptions` which adds a `_metadata` column that can be used for updates. The configured metadata is available through `CrudEntry.Metadata`.
- Add `ignoreEmptyUpdates` option on `TableOptions` which skips creating CRUD entries for updates that don't change any values.
- Reporting progress information about downloaded rows. Sync progress is available through `SyncStatus.DownloadProgress()`.
- Support bucket priorities.
- Report `PriorityStatusEntries` on `SyncStatus`.
- Added ability to specify `AppMetadata` for sync/stream requests.

Note: This requires a PowerSync service version `>=1.17.0` in order for logs to display metadata.

```csharp
db.Connect(connector, new PowerSync.Common.Client.Sync.Stream.PowerSyncConnectionOptions
{
    // This will be included in PowerSync service logs
    AppMetadata = new Dictionary<string, string>
    {
        { "app_version", myAppVersion },
    }
});
```

## 0.0.5-alpha.1

- Using the latest version (0.4.9) of the core extension, it introduces support for the Rust Sync implementation and also makes it the default - users can still opt out and use the legacy C# sync implementation as option when calling `connect()`.

## 0.0.4-alpha.1

- Fixed MAUI issues related to extension loading when installing package outside of the monorepo.

## 0.0.3-alpha.1

- Minor changes to accommodate PowerSync.MAUI package extension.

## 0.0.2-alpha.2

- Updated core extension to v0.3.14
- Loading last synced time from core extension
- Expose upload and download errors on SyncStatus
- Improved credentials management and error handling. Credentials are invalidated when they expire or become invalid based on responses from the PowerSync service. The frequency of credential fetching has been reduced as a result of this work.

## 0.0.2-alpha.1

- Introduce package. Support for Desktop .NET use cases.

### Platform Runtime Support Added

- linux-arm64
- linux-x64
- osx-arm64
- osx-x64
- wind-x64
