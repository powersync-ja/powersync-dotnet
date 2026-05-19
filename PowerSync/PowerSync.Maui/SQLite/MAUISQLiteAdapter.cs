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

    protected override void LoadExtensions(SqliteConnection db)
    {
        db.EnableExtensions(true);

        // The default PowerSync extension's path is resolved for desktop runtimes and
        // does not apply on iOS/MacCatalyst/Android, where the native library lives
        // in a platform-specific location. If the user didn't supply any extensions,
        // load the platform-correct default. Otherwise honor their list — but still
        // intercept the DEFAULT_POWERSYNC_EXTENSION sentinel so consumers can mix
        // the bundled PowerSync extension with their own custom extensions.
        var userExtensions = options.SqliteOptions?.Extensions;
        if (userExtensions == null)
        {
            LoadDefaultPowerSyncExtension(db);
            return;
        }

        foreach (var extension in userExtensions)
        {
            if (ReferenceEquals(extension, SqliteExtension.DEFAULT_POWERSYNC_EXTENSION))
            {
                LoadDefaultPowerSyncExtension(db);
            }
            else
            {
                db.LoadExtension(extension.Path, extension.EntryPoint);
            }
        }
    }

    private static void LoadDefaultPowerSyncExtension(SqliteConnection db)
    {
#if IOS || MACCATALYST
        LoadExtensionApple(db);
#elif ANDROID
        db.LoadExtension("libpowersync");
#else
        var defaultExtension = SqliteExtension.DEFAULT_POWERSYNC_EXTENSION;
        db.LoadExtension(defaultExtension.Path, defaultExtension.EntryPoint);
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

