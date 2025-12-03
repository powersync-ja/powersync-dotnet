namespace PowerSync.Maui.SQLite;

using Microsoft.Data.Sqlite;
using PowerSync.Common.MDSQLite;

// iOS specific imports
#if IOS
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

#if IOS
        LoadExtensionIOS(db);
#elif ANDROID
        db.LoadExtension("libpowersync");
#else
        base.LoadExtension(db);
#endif
    }

    private void LoadExtensionIOS(SqliteConnection db)
    {
#if IOS
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