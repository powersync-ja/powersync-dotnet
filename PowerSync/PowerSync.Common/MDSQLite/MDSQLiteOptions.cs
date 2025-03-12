namespace PowerSync.Common.MDSQLite;

public sealed class TemporaryStorageOption
{
    public static readonly TemporaryStorageOption MEMORY = new("memory");
    public static readonly TemporaryStorageOption FILESYSTEM = new("file");

    public string Value { get; }
    private TemporaryStorageOption(string value) => Value = value;
    public override string ToString() => Value;
    public static implicit operator string(TemporaryStorageOption option) => option.Value;
}

/// <summary>
/// SQLite journal mode. Set on the primary connection.
/// This library is written with WAL mode in mind - other modes may cause
/// unexpected locking behavior.
/// </summary>
public sealed class SqliteJournalMode
{
    /// <summary>
    /// Use a write-ahead log instead of a rollback journal.
    /// This provides good performance and concurrency.
    /// </summary>
    public static readonly SqliteJournalMode WAL = new("WAL");
    public static readonly SqliteJournalMode DELETE = new("DELETE");
    public static readonly SqliteJournalMode TRUNCATE = new("TRUNCATE");
    public static readonly SqliteJournalMode PERSIST = new("PERSIST");
    public static readonly SqliteJournalMode MEMORY = new("MEMORY");
    public static readonly SqliteJournalMode OFF = new("OFF");

    public string Value { get; }

    private SqliteJournalMode(string value) => Value = value;

    public override string ToString() => Value;

    public static implicit operator string(SqliteJournalMode mode) => mode.Value;
}

/// <summary>
/// SQLite file commit mode.
/// </summary>
public sealed class SqliteSynchronous
{
    public static readonly SqliteSynchronous NORMAL = new("NORMAL");
    public static readonly SqliteSynchronous FULL = new("full");
    public static readonly SqliteSynchronous OFF = new("OFF");

    public string Value { get; }
    private SqliteSynchronous(string value) => Value = value;
    public override string ToString() => Value;
    public static implicit operator string(SqliteSynchronous mode) => mode.Value;
}

public class SqliteExtension
{
    public string Path { get; set; } = string.Empty;
    public string? EntryPoint { get; set; }
}

public class MDSQLiteOptions
{
    /// <summary>
    /// SQLite journal mode. Defaults to WAL.
    /// </summary>
    public SqliteJournalMode? JournalMode { get; set; }

    /// <summary>
    /// SQLite synchronous flag. Defaults to NORMAL, which is safe for WAL mode.
    /// </summary>
    public SqliteSynchronous? Synchronous { get; set; }

    /// <summary>
    /// Journal/WAL size limit. Defaults to 6MB.
    /// </summary>
    public int? JournalSizeLimit { get; set; }

    /// <summary>
    /// Timeout in milliseconds waiting for locks to be released by other connections.
    /// Defaults to 30 seconds.
    /// </summary>
    public int? LockTimeoutMs { get; set; }

    /// <summary>
    /// Encryption key for the database.
    /// If set, the database will be encrypted using SQLCipher.
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Where to store SQLite temporary files. Defaults to MEMORY.
    /// </summary>
    public TemporaryStorageOption? TemporaryStorage { get; set; }

    /// <summary>
    /// Maximum SQLite cache size. Defaults to 50MB.
    /// </summary>
    public int? CacheSizeKb { get; set; }

    /// <summary>
    /// Load extensions using the path and entryPoint.
    /// </summary>
    public SqliteExtension[]? Extensions { get; set; }
}

public class RequiredMDSQLiteOptions : MDSQLiteOptions
{
    public static RequiredMDSQLiteOptions DEFAULT_SQLITE_OPTIONS = new()
    {
        JournalMode = SqliteJournalMode.WAL,
        Synchronous = SqliteSynchronous.NORMAL,
        JournalSizeLimit = 6 * 1024 * 1024,
        CacheSizeKb = 50 * 1024,
        TemporaryStorage = TemporaryStorageOption.MEMORY,
        LockTimeoutMs = 30000,
        EncryptionKey = null,
        Extensions = []
    };

    public new SqliteJournalMode JournalMode { get; set; } = null!;

    public new SqliteSynchronous Synchronous { get; set; } = null!;

    public new int JournalSizeLimit { get; set; }

    public new int LockTimeoutMs { get; set; }

    public new string? EncryptionKey { get; set; }

    public new TemporaryStorageOption TemporaryStorage { get; set; } = null!;

    public new int CacheSizeKb { get; set; }
    public new SqliteExtension[] Extensions { get; set; } = null!;
}