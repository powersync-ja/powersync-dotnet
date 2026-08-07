using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

/// <summary>
/// Execute with `dotnet run --project Tools/Setup`
/// </summary>
public class PowerSyncSetup
{
    private const string VERSION = "0.5.2";

    private const string GITHUB_BASE_URL = $"https://github.com/powersync-ja/powersync-sqlite-core/releases/download/v{VERSION}";

    /// <summary>
    /// xcframework slices reachable from our target frameworks. Everything else is removed
    /// by <see cref="TrimXcframework"/>.
    /// </summary>
    private static readonly string[] REQUIRED_APPLE_SLICES =
    [
        "ios-arm64",                      // device
        "ios-arm64_x86_64-simulator",     // simulator
        "ios-arm64_x86_64-maccatalyst"    // MacCatalyst
    ];

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
            await SetupApple();
            await SetupAndroid();
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

    /// <summary>
    /// Downloads the xcframework used by the iOS and MacCatalyst targets. The archive is
    /// self-describing and contains slices for both, so a single copy serves both targets.
    /// </summary>
    public async Task SetupApple()
    {
        Console.WriteLine("Setting up Apple libraries...");

        var nativeDir = Path.Combine(_basePath, "PowerSync.Common", "Platforms", "Apple", "NativeLibs");
        var config = new ArchiveConfig(
            "powersync-sqlite-core.xcframework.zip",
            "powersync-sqlite-core.xcframework"
        );

        await ProcessArchiveDownload(nativeDir, config, GITHUB_BASE_URL);

        var xcframeworkPath = Path.Combine(nativeDir, config.ExtractedName);
        if (Directory.Exists(xcframeworkPath))
        {
            TrimXcframework(xcframeworkPath);
        }
    }

    /// <summary>
    /// Removes xcframework slices and debug symbols that none of our target frameworks can
    /// use. The upstream archive also ships tvOS, watchOS and native macOS slices plus dSYMs
    /// for every slice; together these are ~97% of its size, and they would otherwise be
    /// embedded into the NuGet package four times over (net8/net9 x ios/maccatalyst).
    /// </summary>
    private static void TrimXcframework(string xcframeworkPath)
    {
        var infoPlistPath = Path.Combine(xcframeworkPath, "Info.plist");

        // Parse the DOCTYPE (so Save writes it back) without fetching the external DTD.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = null };
        XDocument plist;
        using (var reader = XmlReader.Create(infoPlistPath, settings))
        {
            plist = XDocument.Load(reader);
        }

        var availableLibraries = plist.Descendants("key")
            .First(key => key.Value == "AvailableLibraries")
            .ElementsAfterSelf("array")
            .First();

        var keptSlices = new List<string>();

        foreach (var slice in availableLibraries.Elements("dict").ToList())
        {
            var identifier = PlistValue(slice, "LibraryIdentifier")
                ?? throw new Exception("xcframework slice has no LibraryIdentifier");
            var sliceDir = Path.Combine(xcframeworkPath, identifier);

            if (!REQUIRED_APPLE_SLICES.Contains(identifier))
            {
                DeleteDirectoryIfExists(sliceDir);
                slice.Remove();
                continue;
            }

            var debugSymbolsPath = PlistValue(slice, "DebugSymbolsPath");
            if (debugSymbolsPath != null)
            {
                DeleteDirectoryIfExists(Path.Combine(sliceDir, debugSymbolsPath));
                RemovePlistEntry(slice, "DebugSymbolsPath");
            }

            keptSlices.Add(identifier);
        }

        // Fail loudly rather than shipping a package that silently lost a platform, in case
        // upstream renames a slice.
        var missing = REQUIRED_APPLE_SLICES.Except(keptSlices).ToList();
        if (missing.Count > 0)
        {
            throw new Exception($"xcframework is missing required slice(s): {string.Join(", ", missing)}");
        }

        // XDocument otherwise round-trips the empty internal DTD subset as "[]", and prepends
        // a BOM. plutil rejects both.
        if (plist.DocumentType != null)
        {
            plist.DocumentType.InternalSubset = null;
        }

        var writerSettings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        using (var writer = XmlWriter.Create(infoPlistPath, writerSettings))
        {
            plist.Save(writer);
        }

        Console.WriteLine($"✓ Trimmed xcframework to: {string.Join(", ", keptSlices)}");
    }

    private static string? PlistValue(XElement dict, string key) =>
        dict.Elements("key")
            .FirstOrDefault(element => element.Value == key)
            ?.ElementsAfterSelf().FirstOrDefault()?.Value;

    private static void RemovePlistEntry(XElement dict, string key)
    {
        var keyElement = dict.Elements("key").First(element => element.Value == key);
        keyElement.ElementsAfterSelf().First().Remove();
        keyElement.Remove();
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    public async Task SetupAndroid()
    {
        Console.WriteLine("Setting up Android libraries...");

        var nativeDir = Path.Combine(_basePath, "PowerSync.Common", "Platforms", "Android", "jniLibs");

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
