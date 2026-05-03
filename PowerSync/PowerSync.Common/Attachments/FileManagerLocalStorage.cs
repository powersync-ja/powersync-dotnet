namespace PowerSync.Common.Attachments;

/// <summary>
/// Default <see cref="ILocalStorageAdapter"/> backed by <see cref="System.IO"/>.
/// Files are written under <see cref="AttachmentsDirectory"/>.
/// </summary>
public sealed class FileManagerLocalStorage : ILocalStorageAdapter
{
    /// <summary>
    /// Creates a <see cref="FileManagerLocalStorage"/> that stores files under <paramref name="attachmentsDirectory"/>.
    /// The directory is created on <see cref="InitializeAsync"/>.
    /// </summary>
    public FileManagerLocalStorage(string attachmentsDirectory)
    {
        if (string.IsNullOrWhiteSpace(attachmentsDirectory))
        {
            throw new ArgumentException("attachmentsDirectory must be a non-empty path.", nameof(attachmentsDirectory));
        }

        AttachmentsDirectory = attachmentsDirectory;
    }

    /// <summary>Gets the directory under which attachment files are stored.</summary>
    public string AttachmentsDirectory { get; }

    /// <inheritdoc/>
    public async Task<long> SaveFileAsync(string filePath, Stream data)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var output = File.Create(filePath);
        await data.CopyToAsync(output);
        return output.Length;
    }

    /// <inheritdoc/>
    public Task<Stream> ReadFileAsync(string filePath)
    {
        return !File.Exists(filePath)
            ? throw new FileNotFoundException($"Attachment file not found at path: {filePath}", filePath)
            : Task.FromResult<Stream>(File.OpenRead(filePath));
    }

    /// <inheritdoc/>
    public Task DeleteFileAsync(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> FileExistsAsync(string filePath) => Task.FromResult(File.Exists(filePath));

    /// <inheritdoc/>
    public Task CreateDirectoryAsync(string path)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveDirectoryAsync(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(AttachmentsDirectory);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ClearAsync()
    {
        if (Directory.Exists(AttachmentsDirectory))
        {
            Directory.Delete(AttachmentsDirectory, recursive: true);
        }

        Directory.CreateDirectory(AttachmentsDirectory);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public string GetLocalUri(string filename) => Path.Combine(AttachmentsDirectory, filename);
}
