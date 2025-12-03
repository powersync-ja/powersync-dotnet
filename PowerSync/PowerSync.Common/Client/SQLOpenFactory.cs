namespace PowerSync.Common.Client;

using PowerSync.Common.DB;

public class SQLOpenOptions : IDatabaseSource
{
    /// <summary>
    /// Filename for the database.
    /// </summary>
    public string DbFilename { get; set; } = "";

    /// <summary>
    /// Directory where the database file is located.
    /// </summary>
    public string? DbLocation { get; set; }
}

public interface ISQLOpenFactory: IDatabaseSource
{
    /// <summary>
    /// Opens a connection adapter to a SQLite Database.
    /// </summary>
    IDBAdapter OpenDatabase();
}
