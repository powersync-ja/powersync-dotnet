namespace PowerSync.Maui.SQLite;

using Microsoft.Data.Sqlite;

using PowerSync.Common.MDSQLite;

// iOS/MacCatalyst specific imports
#if IOS || MACCATALYST
using Foundation;
#endif

public class MAUISQLiteAdapter : MDSQLiteAdapter
{
    public MAUISQLiteAdapter(MDSQLiteAdapterOptions options) : base(options)
    {
    }

    // The bundled PowerSync extension lives in a platform-specific location on
    // iOS/MacCatalyst/Android — the desktop runtime path used by the base class
    // does not resolve to it. Override only the PowerSync-extension load hook;
    // user-supplied custom extensions still flow through MDSQLiteAdapter.LoadExtensions
    // unchanged, so consumers can freely combine the bundled extension (via the
    // LoadPowerSyncExtension flag) with their own.
    protected override void LoadDefaultPowerSyncExtension(SqliteConnection db)
    {
#if IOS || MACCATALYST
        LoadExtensionApple(db);
#elif ANDROID
        db.LoadExtension("libpowersync");
#else
        base.LoadDefaultPowerSyncExtension(db);
#endif
    }

    private static void LoadExtensionApple(SqliteConnection db)
    {
#if IOS || MACCATALYST
        var bundlePath = Foundation.NSBundle.FromIdentifier("co.powersync.sqlitecore")?.BundlePath;
        if (bundlePath == null)
        {
            throw new Exception("Could not find PowerSync SQLite extension bundle path");
        }

        var filePath =
            Path.Combine(bundlePath, "powersync-sqlite-core");

        using var loadExtension = db.CreateCommand();
        loadExtension.CommandText = "SELECT load_extension(@path, @entryPoint)";
        loadExtension.Parameters.AddWithValue("@path", filePath);
        loadExtension.Parameters.AddWithValue("@entryPoint", "sqlite3_powersync_init");
        loadExtension.ExecuteNonQuery();
#endif
    }
}

