namespace PowerSync.Common.Tests.Utils;

public static class DatabaseUtils
{
    public static void CleanDb(string path)
    {
        TryDelete(path);
        TryDelete($"{path}-shm");
        TryDelete($"{path}-wal");
    }

    private static void TryDelete(string filePath)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists)
            return;

        const int retryCount = 3;
        int attempt = 0;

        while (attempt < retryCount)
        {
            try
            {
                file.Delete();
                file.Refresh(); // force state update
                if (!file.Exists)
                    return;
            }
            catch (IOException)
            {
                attempt++;
                Thread.Sleep(100);
            }
        }

        Console.Error.WriteLine($"Failed to delete file after {retryCount} attempts: {filePath}");
    }
}