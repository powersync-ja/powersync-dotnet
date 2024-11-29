using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

public class Setup
{
    static async Task Main(string[] args)
    {
        const string baseUrl = "https://github.com/powersync-ja/powersync-sqlite-core/releases/download/v0.3.8";
        const string powersyncCorePath = "../../PowerSync/PowerSync.Common";

        string rid = GetRuntimeIdentifier();
        string nativeDir = Path.Combine(powersyncCorePath, "runtimes", rid, "native");

        Directory.CreateDirectory(nativeDir);

        string sqliteCoreFilename = GetLibraryForPlatform();
        string sqliteCorePath = Path.Combine(nativeDir, sqliteCoreFilename);

        try
        {
            await DownloadFile($"{baseUrl}/{sqliteCoreFilename}", sqliteCorePath);

            string newFileName = GetFileNameForPlatform();
            string newFilePath = Path.Combine(nativeDir, newFileName);

            if (File.Exists(sqliteCorePath))
            {
                File.Move(sqliteCorePath, newFilePath, overwrite: true);
                Console.WriteLine($"File renamed successfully from {sqliteCoreFilename} to {newFileName}");
            }
            else
            {
                throw new IOException($"File {sqliteCoreFilename} does not exist.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "osx-arm64";
            else
                return "osx-x64";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                return "linux-arm64";
            else
                return "linux-x64";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "win-x64";
        }
        throw new PlatformNotSupportedException("Unsupported platform.");
    }

    static string GetFileNameForPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "libpowersync.dylib";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "libpowersync.so";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "powersync.dll";
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported platform.");
        }
    }

    static string GetLibraryForPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "libpowersync_aarch64.dylib"
                : "libpowersync_x64.dylib";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "libpowersync_aarch64.so"
                : "libpowersync_x64.so";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "powersync_x64.dll";
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported platform.");
        }
    }

    static async Task DownloadFile(string url, string outputPath)
    {
        Console.WriteLine($"Downloading: {url}");

        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to download file: {response.StatusCode} {response.ReasonPhrase}");
        }

        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream);

        Console.WriteLine($"Downloaded to {outputPath}");
    }
}