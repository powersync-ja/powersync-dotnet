# PowerSync.Common Changelog

## 0.0.8-alpha.1
- Add support for [sync streams](https://docs.powersync.com/sync/streams/overview).
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
- Altered query methods, `Execute()`, `GetAll()`, `GetOptional()`, `Get()`, `Watch()`,  to support null parameters in their parameters list, for example: 

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
* linux-arm64
* linux-x64
* osx-arm64
* osx-x64
* wind-x64
