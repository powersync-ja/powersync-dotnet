namespace PowerSync.Common.Tests.MDSQLite;

using Microsoft.Data.Sqlite;

using PowerSync.Common.Client;
using PowerSync.Common.MDSQLite;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Utils;

/// <summary>
/// dotnet test -v n --framework net8.0 --filter "MDSQLiteAdapterTests"
/// </summary>
[Collection("MDSQLiteAdapterTests")]
public class MDSQLiteAdapterTests
{
    private class AssetResult
    {
        public string id { get; set; } = "";
        public string description { get; set; } = "";
        public string? make { get; set; }
    }

    [Fact]
    public async Task DisablingCoreExtensionPreventsPowerSyncFromLoading()
    {
        var dbName = $"MDSQLiteAdapter-{Guid.NewGuid():N}.db";
        var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new MDSQLiteDBOpenFactory(new MDSQLiteOpenFactoryOptions
            {
                DbFilename = dbName,
                SqliteOptions = new MDSQLiteOptions
                {
                    LoadPowerSyncExtension = false,
                    Extensions = [],
                },
            }),
            Schema = TestSchema.AppSchema,
        });

        try
        {
            // Without the PowerSync extension, `powersync_init()` is not a registered function.
            await Assert.ThrowsAsync<SqliteException>(async () => await db.Init());
        }
        finally
        {
            try { await db.Close(); } catch { /* expected — init failed */ }
            DatabaseUtils.CleanDb(dbName);
        }
    }

    [Fact]
    public async Task LoadsCustomPowerSyncExtensionFromOverriddenPath()
    {
        var dbName = $"MDSQLiteAdapter-{Guid.NewGuid():N}.db";
        var sourcePath = PowerSyncPathResolver.GetNativeLibraryPath(AppContext.BaseDirectory);
        var customPath = Path.Combine(
            Path.GetTempPath(),
            $"powersync-ext-copy-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}"
        );
        File.Copy(sourcePath, customPath, overwrite: true);

        var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new MDSQLiteDBOpenFactory(new MDSQLiteOpenFactoryOptions
            {
                DbFilename = dbName,
                SqliteOptions = new MDSQLiteOptions
                {
                    LoadPowerSyncExtension = false,
                    Extensions = [
                        new SqliteExtension { Path = customPath, EntryPoint = "sqlite3_powersync_init" },
                    ],
                },
            }),
            Schema = TestSchema.AppSchema,
        });

        try
        {
            await db.Init();

            var id = await TestUtils.InsertRandomAsset(db);
            var rows = await db.GetAll<AssetResult>("SELECT id, description, make FROM assets");
            Assert.Single(rows);
            Assert.Equal(id, rows[0].id);
        }
        finally
        {
            await db.Close();
            DatabaseUtils.CleanDb(dbName);
            try { File.Delete(customPath); } catch { /* best-effort cleanup */ }
        }
    }
}
