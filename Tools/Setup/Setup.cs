using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO.Compression;

/// <summary>
/// Execute with `dotnet run --project Tools/Setup`
/// </summary>
public class PowerSyncSetup
{
    private const string VERSION = "0.4.13";

    private const string GITHUB_BASE_URL = $"https://github.com/powersync-ja/powersync-sqlite-core/releases/download/v{VERSION}";

    private readonly HttpClient _httpClient;
    private readonly string _basePath;

    public PowerSyncSetup()
    {
        _httpClient = new HttpClient();
        _basePath = Path.Combine(AppContext.BaseDirectory, "../../../../..", "PowerSync");
    }

    public async Task RunSetup()
    {
        try
        {
            await SetupDesktop();
            await SetupMauiIos();
            await SetupMauiAndroid();
            await SetupMauiMacCatalyst();
        }
        finally
        {
            _httpClient?.Dispose();
        }
    }

    public async Task SetupDesktop()
    {
        Console.WriteLine("Setting up Desktop libraries...");

        var runtimeConfigs = GetDesktopRuntimeConfigs();
        var commonPath = Path.Combine(_basePath, "PowerSync.Common");

        foreach (var config in runtimeConfigs)
        {
            await ProcessDesktopRuntime(commonPath, config);
        }
    }

    private static Dictionary<string, RuntimeConfig> GetDesktopRuntimeConfigs()
    {
        return new Dictionary<string, RuntimeConfig>
        {
            { "osx-x64", new RuntimeConfig("libpowersync_x64.macos.dylib", "libpowersync.dylib") },
            { "osx-arm64", new RuntimeConfig("libpowersync_aarch64.macos.dylib", "libpowersync.dylib") },
            { "linux-x64", new RuntimeConfig("libpowersync_x64.linux.so", "libpowersync.so") },
            { "linux-arm64", new RuntimeConfig("libpowersync_aarch64.linux.so", "libpowersync.so") },
            { "win-x64", new RuntimeConfig("powersync_x64.dll", "powersync.dll") },
            { "win-arm64", new RuntimeConfig("powersync_aarch64.dll", "powersync.dll") }
        };
    }

    private async Task ProcessDesktopRuntime(string basePath, KeyValuePair<string, RuntimeConfig> runtimeConfig)
    {
        var (rid, config) = runtimeConfig;
        var nativeDir = Path.Combine(basePath, "runtimes", rid, "native");

        try
        {
            Directory.CreateDirectory(nativeDir);

            var downloadPath = Path.Combine(nativeDir, config.OriginalFileName);
            var finalPath = Path.Combine(nativeDir, config.FinalFileName);

            var downloadUrl = $"{GITHUB_BASE_URL}/{config.OriginalFileName}";

            await DownloadFile(downloadUrl, downloadPath);
            File.Move(downloadPath, finalPath, overwrite: true);

            Console.WriteLine($"✓ {rid}: {config.OriginalFileName} → {config.FinalFileName}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Failed to process {rid}: {ex.Message}");
        }
    }

    public async Task SetupMauiIos()
    {
        Console.WriteLine("Setting up MAUI iOS libraries...");

        var nativeDir = Path.Combine(_basePath, "PowerSync.Maui", "Platforms", "iOS", "NativeLibs");
        var config = new ArchiveConfig(
            "powersync-sqlite-core.xcframework.zip",
            "powersync-sqlite-core.xcframework"
        );

        await ProcessArchiveDownload(nativeDir, config, GITHUB_BASE_URL);
    }

    public async Task SetupMauiAndroid()
    {
        Console.WriteLine("Setting up MAUI Android libraries...");

        var nativeDir = Path.Combine(_basePath, "PowerSync.Maui", "Platforms", "Android", "jniLibs");

        try
        {
            Directory.CreateDirectory(nativeDir);

            await Task.WhenAll(
                DownloadAndroidLibrary("libpowersync_aarch64.android.so ", nativeDir,"arm64-v8a"),
                DownloadAndroidLibrary("libpowersync_armv7.android.so ", nativeDir, "armeabi-v7a"),
                DownloadAndroidLibrary("libpowersync_x86.android.so ", nativeDir, "x86"),
                DownloadAndroidLibrary("libpowersync_x64.android.so ", nativeDir, "x86_64")
            );

            Console.WriteLine($"✓ Android: Downloaded native libraries");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Failed to setup Android: {ex.Message}");
        }
    }

    private async Task DownloadAndroidLibrary(string filename, string jniLibsDir, string arch)
    {
        var targetDir = Path.Combine(jniLibsDir, arch);
		Directory.CreateDirectory(targetDir);
        var targetFile = Path.Combine(targetDir, "libpowersync.so");
        await DownloadFile($"{GITHUB_BASE_URL}/{filename}", targetFile);
    }

    public async Task SetupMauiMacCatalyst()
    {
        Console.WriteLine("Setting up MAUI MacCatalyst libraries...");

        var nativeDir = Path.Combine(_basePath, "PowerSync.Maui", "Platforms", "MacCatalyst", "NativeLibs");
        var config = new ArchiveConfig(
            "powersync-sqlite-core.xcframework.zip",
            "powersync-sqlite-core.xcframework"
        );

        await ProcessArchiveDownload(nativeDir, config, GITHUB_BASE_URL);
    }

    private async Task ProcessArchiveDownload(string nativeDir, ArchiveConfig config, string baseUrl)
    {
        try
        {
            Directory.CreateDirectory(nativeDir);

            var downloadPath = Path.Combine(nativeDir, config.ArchiveFileName);
            var extractedPath = Path.Combine(nativeDir, config.ExtractedName);
            var downloadUrl = $"{baseUrl}/{config.ArchiveFileName}";

            await DownloadFile(downloadUrl, downloadPath);

            // Clean up existing extraction
            if (Directory.Exists(extractedPath))
                Directory.Delete(extractedPath, recursive: true);

            ExtractZipPreservingSymlinks(downloadPath, nativeDir);
            File.Delete(downloadPath);

            Console.WriteLine($"✓ Extracted {config.ArchiveFileName} → {config.ExtractedName}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Failed to process archive: {ex.Message}");
        }
    }

    private static void ExtractZipPreservingSymlinks(string zipPath, string destDir)
    {
        // ZipFile.ExtractToDirectory does not preserve symlinks, which breaks
        // macOS/Catalyst .xcframework bundles. Use `unzip` on Unix instead.
        if (!OperatingSystem.IsWindows())
        {
            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "unzip",
                ArgumentList = { "-o", zipPath, "-d", destDir },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new Exception($"unzip exited with code {proc.ExitCode}: {proc.StandardError.ReadToEnd()}");
        }
        else
        {
            ZipFile.ExtractToDirectory(zipPath, destDir);
        }
    }

    private async Task DownloadFile(string url, string outputPath)
    {
        Console.WriteLine($"📥 Downloading: {Path.GetFileName(outputPath)}");

        using var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Download failed: {response.StatusCode} {response.ReasonPhrase}");
        }

        await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream);
    }

    private static void CleanupPaths(params string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                else if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Cleanup warning for {path}: {ex.Message}");
            }
        }
    }

    private record RuntimeConfig(string OriginalFileName, string FinalFileName);
    private record ArchiveConfig(string ArchiveFileName, string ExtractedName);
}

public class Program
{
    static async Task Main(string[] args)
    {
        var setup = new PowerSyncSetup();
        await setup.RunSetup();
    }
}
