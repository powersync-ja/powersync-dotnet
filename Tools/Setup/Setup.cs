using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

public class Setup
{
    static async Task Main(string[] args)
    {
        const string baseUrl = "https://github.com/powersync-ja/powersync-sqlite-core/releases/download/v0.3.14";
        string powersyncCorePath = Path.Combine(AppContext.BaseDirectory, "../../../../..", "PowerSync/PowerSync.Common/");

        var runtimeIdentifiers = new Dictionary<string, (string originalFile, string newFile)>
        {
            { "osx-x64", ("libpowersync_x64.dylib", "libpowersync.dylib") },
            { "osx-arm64", ("libpowersync_aarch64.dylib", "libpowersync.dylib") },
            { "linux-x64", ("libpowersync_x64.so", "libpowersync.so") },
            { "linux-arm64", ("libpowersync_aarch64.so", "libpowersync.so") },
            { "win-x64", ("powersync_x64.dll", "powersync.dll") }
        };

        foreach (var (rid, (originalFile, newFile)) in runtimeIdentifiers)
        {
            string nativeDir = Path.Combine(powersyncCorePath, "runtimes", rid, "native");
            Directory.CreateDirectory(nativeDir);

            string sqliteCorePath = Path.Combine(nativeDir, originalFile);
            string newFilePath = Path.Combine(nativeDir, newFile);

            try
            {
                await DownloadFile($"{baseUrl}/{originalFile}", sqliteCorePath);

                if (File.Exists(sqliteCorePath))
                {
                    File.Move(sqliteCorePath, newFilePath, overwrite: true);
                    Console.WriteLine($"File renamed successfully from {originalFile} to {newFile} in {nativeDir}");
                }
                else
                {
                    throw new IOException($"File {originalFile} does not exist.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error processing {rid}: {ex.Message}");
            }
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