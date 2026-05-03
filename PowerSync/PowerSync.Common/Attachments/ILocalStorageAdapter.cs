namespace PowerSync.Common.Attachments;

/// <summary>
/// Local file storage operations used by <see cref="AttachmentQueue"/>.
/// Implementations handle file I/O, directory management, and storage initialization.
/// </summary>
public interface ILocalStorageAdapter
{
    /// <summary>
    /// Saves a stream to a local file.
    /// </summary>
    /// <param name="filePath">Absolute path where the file will be stored.</param>
    /// <param name="data">Stream of bytes to write.</param>
    /// <returns>Number of bytes written to the file.</returns>
    /// <remarks>The caller owns and disposes the input stream.</remarks>
    Task<long> SaveFileAsync(string filePath, Stream data);

    /// <summary>
    /// Opens a file for streaming reads.
    /// </summary>
    /// <param name="filePath">Absolute path of the file.</param>
    /// <returns>A readable stream over the file's contents.</returns>
    /// <remarks>The caller disposes the returned stream.</remarks>
    Task<Stream> ReadFileAsync(string filePath);

    /// <summary>
    /// Deletes the file at the given path. No-ops if the file doesn't exist.
    /// </summary>
    /// <param name="filePath">Absolute path of the file to delete.</param>
    /// <returns>A task that completes when the file has been deleted or if it didn't exist.</returns>
    Task DeleteFileAsync(string filePath);

    /// <summary>
    /// Checks if a file exists at the given path.
    /// </summary>
    /// <param name="filePath">Absolute path of the file.</param>
    /// <returns>True if the file exists, false otherwise.</returns>
    Task<bool> FileExistsAsync(string filePath);

    /// <summary>
    /// Creates a directory at the specified path. No-ops if it already exists.
    /// </summary>
    /// <param name="path">The full path to the directory.</param>
    /// <returns>A task that completes when the directory has been created or already exists.</returns>
    Task CreateDirectoryAsync(string path);

    /// <summary>
    /// Removes a directory and all its contents at the specified path.
    /// </summary>
    /// <param name="path">The full path to the directory.</param>
    /// <returns>A task that completes when the directory and its contents have been removed.</returns>
    Task RemoveDirectoryAsync(string path);

    /// <summary>
    /// Initializes the storage adapter (e.g., creating necessary directories).
    /// </summary>
    /// <returns>A task that completes when initialization is done.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Clears all files in the storage.
    /// </summary>
    /// <returns>A task that completes when all files have been cleared.</returns>
    Task ClearAsync();

    /// <summary>
    /// Returns the file path for the provided filename in the storage directory.
    /// </summary>
    /// <param name="filename">The name of the file.</param>
    /// <returns>The full file path where the file is stored.</returns>
    string GetLocalUri(string filename);
}
