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

    protected override void LoadExtension(SqliteConnection db)
    {
        db.EnableExtensions(true);

#if IOS || MACCATALYST
        LoadExtensionApple(db);
#elif ANDROID
        db.LoadExtension("libpowersync");
#else
        base.LoadExtension(db);
#endif
    }

    private void LoadExtensionApple(SqliteConnection db)
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

