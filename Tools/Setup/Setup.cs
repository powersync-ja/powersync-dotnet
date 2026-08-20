using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

/// <summary>
/// Downloads the powersync-sqlite-core native libraries into PowerSync.Common/runtimes,
/// laid out using NuGet's RID conventions so a single package can serve every target:
///
///   runtimes/{rid}/native/...            desktop and Android, resolved by RID
///   runtimes/ios/native/*.xcframework.zip
///   runtimes/maccatalyst/native/*.xcframework.zip
///
/// The Apple slices stay zipped on purpose. NuGet pack/extract and ordinary file copies
/// drop symlinks, which corrupts the versioned MacCatalyst framework bundle and makes
/// codesign fail with "bundle format is ambiguous". The Apple SDK unzips these itself
/// and embeds the framework into App.app/Frameworks.
///
/// Execute with `dotnet run --project Tools/Setup`
/// </summary>
public class PowerSyncSetup
{
    private const string VERSION = "0.5.2";

    private const string GITHUB_BASE_URL = $"https://github.com/powersync-ja/powersync-sqlite-core/releases/download/v{VERSION}";

    private const string XCFRAMEWORK_NAME = "powersync-sqlite-core.xcframework";

    private readonly HttpClient _httpClient;
    private readonly string _commonPath;

    public PowerSyncSetup()
    {
        _httpClient = new HttpClient();
        _commonPath = Path.Combine(AppContext.BaseDirectory, "../../../../..", "PowerSync", "PowerSync.Common");
    }

    public async Task RunSetup()
    {
        try
        {
            await SetupRuntimes();
            await SetupApple();
        }
        finally
        {
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Every target whose native library is a single downloadable file: one entry per RID.
    /// </summary>
    public async Task SetupRuntimes()
    {
        Console.WriteLine("Setting up native libraries...");

        foreach (var config in GetRuntimeConfigs())
        {
            await ProcessRuntime(config);
        }
    }

    private static Dictionary<string, RuntimeConfig> GetRuntimeConfigs()
    {
        return new Dictionary<string, RuntimeConfig>
        {
            // Desktop
            { "osx-x64", new RuntimeConfig("libpowersync_x64.macos.dylib", "libpowersync.dylib") },
            { "osx-arm64", new RuntimeConfig("libpowersync_aarch64.macos.dylib", "libpowersync.dylib") },
            { "linux-x64", new RuntimeConfig("libpowersync_x64.linux.so", "libpowersync.so") },
            { "linux-arm64", new RuntimeConfig("libpowersync_aarch64.linux.so", "libpowersync.so") },
            { "win-x64", new RuntimeConfig("powersync_x64.dll", "powersync.dll") },
            { "win-arm64", new RuntimeConfig("powersync_aarch64.dll", "powersync.dll") },

            // Android. These reach the APK's ABI directories via AndroidNativeLibrary items
            // in PowerSync.Common.csproj (ProjectReference) and NuGet RID resolution (PackageReference).
            { "android-arm64", new RuntimeConfig("libpowersync_aarch64.android.so", "libpowersync.so") },
            { "android-x64", new RuntimeConfig("libpowersync_x64.android.so", "libpowersync.so") },
            { "android-arm", new RuntimeConfig("libpowersync_armv7.android.so", "libpowersync.so") },
            { "android-x86", new RuntimeConfig("libpowersync_x86.android.so", "libpowersync.so") },
        };
    }

    private async Task ProcessRuntime(KeyValuePair<string, RuntimeConfig> runtimeConfig)
    {
        var (rid, config) = runtimeConfig;
        var nativeDir = Path.Combine(_commonPath, "runtimes", rid, "native");

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

    /// <summary>
    /// Apple ships one xcframework covering every Apple platform, so it needs its own path:
    /// download once, then write a per-platform copy carrying only the slices that platform
    /// needs. The published archive is ~135MB — it includes tvOS, watchOS and macOS slices we
    /// do not target, plus a dSYM per slice — which trims down to a few MB.
    /// </summary>
    public async Task SetupApple()
    {
        Console.WriteLine("Setting up Apple libraries...");

        var tempZip = Path.Combine(Path.GetTempPath(), $"{XCFRAMEWORK_NAME}.zip");

        try
        {
            await DownloadFile($"{GITHUB_BASE_URL}/{XCFRAMEWORK_NAME}.zip", tempZip);

            WriteTrimmedXcframework(tempZip, "ios", ["ios-arm64", "ios-arm64_x86_64-simulator"]);
            WriteTrimmedXcframework(tempZip, "maccatalyst", ["ios-arm64_x86_64-maccatalyst"]);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Failed to process Apple libraries: {ex.Message}");
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }

    private void WriteTrimmedXcframework(string sourceZip, string rid, string[] keepSlices)
    {
        var nativeDir = Path.Combine(_commonPath, "runtimes", rid, "native");
        Directory.CreateDirectory(nativeDir);

        var targetZip = Path.Combine(nativeDir, $"{XCFRAMEWORK_NAME}.zip");
        if (File.Exists(targetZip)) File.Delete(targetZip);

        using var source = ZipFile.OpenRead(sourceZip);
        using var targetStream = new FileStream(targetZip, FileMode.Create, FileAccess.Write, FileShare.None);
        using var target = new ZipArchive(targetStream, ZipArchiveMode.Create);

        var infoPlistPath = $"{XCFRAMEWORK_NAME}/Info.plist";

        foreach (var entry in source.Entries)
        {
            // Debug symbols are the bulk of the download and are not needed to build against.
            if (entry.FullName.Contains("/dSYMs/")) continue;

            var slice = GetSliceName(entry.FullName);
            if (slice != null && !keepSlices.Contains(slice)) continue;

            // Copying the raw bytes plus ExternalAttributes is what preserves symlinks: they
            // are stored as entries whose content is the link target and whose unix mode bits
            // live in ExternalAttributes. Extracting and re-zipping would lose them.
            var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
            copy.ExternalAttributes = entry.ExternalAttributes;
            copy.LastWriteTime = entry.LastWriteTime;

            using var output = copy.Open();

            if (entry.FullName == infoPlistPath)
            {
                using var plistStream = entry.Open();
                using var trimmed = TrimInfoPlist(plistStream, keepSlices);
                trimmed.CopyTo(output);
            }
            else
            {
                using var input = entry.Open();
                input.CopyTo(output);
            }
        }

        Console.WriteLine($"✓ {rid}: {XCFRAMEWORK_NAME}.zip ({string.Join(", ", keepSlices)})");
    }

    /// <summary>
    /// Returns the xcframework slice a zip entry belongs to, or null for entries that sit
    /// outside a slice (the root Info.plist, LICENSE, README).
    /// </summary>
    private static string? GetSliceName(string entryPath)
    {
        var parts = entryPath.Split('/');
        if (parts.Length < 3 || parts[0] != XCFRAMEWORK_NAME) return null;
        return parts[1];
    }

    /// <summary>
    /// Drops the removed slices from the xcframework's AvailableLibraries list, so the Apple
    /// SDK does not go looking for slices that are no longer present in the archive.
    /// </summary>
    private static Stream TrimInfoPlist(Stream plistStream, string[] keepSlices)
    {
        // XmlResolver is null so the Apple DTD named by the DOCTYPE is never fetched, while
        // DtdProcessing.Parse keeps the DOCTYPE itself in the output.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        using var reader = XmlReader.Create(plistStream, settings);
        var doc = XDocument.Load(reader);

        var librariesArray = doc.Root?.Element("dict")
            ?.Elements("key")
            .FirstOrDefault(k => k.Value == "AvailableLibraries")
            ?.NextNode as XElement;

        librariesArray?.Elements("dict")
            .Where(d => !keepSlices.Contains(PlistString(d, "LibraryIdentifier")))
            .Remove();

        var output = new MemoryStream();
        doc.Save(output);
        output.Position = 0;
        return output;
    }

    private static string? PlistString(XElement dict, string key)
    {
        var keyElement = dict.Elements("key").FirstOrDefault(k => k.Value == key);
        return (keyElement?.NextNode as XElement)?.Value;
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

    private record RuntimeConfig(string OriginalFileName, string FinalFileName);
}

public class Program
{
    static async Task Main(string[] args)
    {
        var setup = new PowerSyncSetup();
        await setup.RunSetup();
    }
}
