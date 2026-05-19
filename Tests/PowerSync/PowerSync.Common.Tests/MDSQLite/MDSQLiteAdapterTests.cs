namespace PowerSync.Common.Tests.MDSQLite;

using Microsoft.Data.Sqlite;

using PowerSync.Common.Client;
using PowerSync.Common.MDSQLite;
using PowerSync.Common.Tests.Utils;

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

    private static PowerSyncDatabase BuildDbWithExtensions(string dbFilename, SqliteExtension[] extensions)
    {
        return new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new MDSQLiteDBOpenFactory(new MDSQLiteOpenFactoryOptions
            {
                DbFilename = dbFilename,
                SqliteOptions = new MDSQLiteOptions { Extensions = extensions },
            }),
            Schema = TestSchema.AppSchema,
        });
    }

    private static string CopyDefaultExtensionToTempPath()
    {
        var sourcePath = SqliteExtension.DEFAULT_POWERSYNC_EXTENSION.Path;
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"powersync-ext-copy-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}"
        );
        File.Copy(sourcePath, tempPath, overwrite: true);
        return tempPath;
    }

    [Fact]
    public async Task EmptyExtensionsArrayDoesNotLoadPowerSync()
    {
        var name = $"MDSQLiteAdapter-ext-empty-{Guid.NewGuid():N}.db";
        var db = BuildDbWithExtensions(name, []);

        try
        {
            // Without the PowerSync extension, `powersync_init()` is not a registered function.
            await Assert.ThrowsAsync<SqliteException>(async () => await db.Init());
        }
        finally
        {
            try { await db.Close(); } catch { /* expected — init failed */ }
            DatabaseUtils.CleanDb(name);
        }
    }

    [Fact]
    public async Task LoadsCustomPowerSyncExtensionFromOverriddenPath()
    {
        var name = $"MDSQLiteAdapter-ext-custom-{Guid.NewGuid():N}.db";
        var customPath = CopyDefaultExtensionToTempPath();
        var db = BuildDbWithExtensions(name, [
            new SqliteExtension { Path = customPath, EntryPoint = "sqlite3_powersync_init" },
        ]);

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
            DatabaseUtils.CleanDb(name);
            try { File.Delete(customPath); } catch { /* best-effort cleanup */ }
        }
    }
}
