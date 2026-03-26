# PowerSync SDK .NET MAUI

This package provides .NET Multi-platform App UI (MAUI) integration for PowerSync, designed to work with the PowerSync.Common package for cross-platform mobile and desktop applications.

## ⚠️ Project Status & Release Note

This package is in beta and is considered ready for production use for tested use cases. See our feature status definitions [here](https://docs.powersync.com/resources/feature-status).

## Installation

This package is published on [NuGet](https://www.nuget.org/packages/PowerSync.Maui) and requires PowerSync.Common to also be installed.

```bash
dotnet add package PowerSync.Maui
dotnet add package PowerSync.Common
```

## Usage

Initialization differs slightly from our Common SDK when using MAUI.

```csharp

private record ListResult(string id, string name, string owner_id, string created_at);

static async Task Main() {

    // Ensures the DB file is stored in a platform appropriate location
    var dbPath = Path.Combine(FileSystem.AppDataDirectory, "maui-example.db");
    var factory = new MAUISQLiteDBOpenFactory(new MDSQLiteOpenFactoryOptions()
    {
        DbFilename = dbPath
    });

    var Db = new PowerSyncDatabase(new PowerSyncDatabaseOptions()
    {
        Database = factory, // Supply a factory
        Schema = AppSchema.PowerSyncSchema,
    });

    await db.Init();

    var lists = await db.GetAll<ListResult>("select * from lists");
}
```
