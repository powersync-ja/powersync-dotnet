using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO.Compression;


public class Setup
{
    
 	static readonly string VERSION = "0.3.14";
    static readonly string BASE_URL = $"https://github.com/powersync-ja/powersync-sqlite-core/releases/download/v{VERSION}";
    
    static async Task Main(string[] args)
    {
        //await DesktopSetup();
        //await MauiIosSetup();
		await MauiAndroidSetup();
    }
	
	static async Task MauiAndroidSetup()
	{
    	string powersyncMauiPath = Path.Combine(AppContext.BaseDirectory, "../../../../..", "PowerSync/PowerSync.Maui/");

	    string nativeDir = Path.Combine(powersyncMauiPath, "Platforms", "Android", "jniLibs");
    	Directory.CreateDirectory(nativeDir);

    	string aarFile = $"powersync-sqlite-core-{VERSION}.aar";
    	string mavenUrl = $"https://repo1.maven.org/maven2/co/powersync/powersync-sqlite-core/{VERSION}/{aarFile}";
    	string aarPath = Path.Combine(nativeDir, aarFile);
    	string extractedDir = Path.Combine(nativeDir, "extracted");

    	try
    	{
        await DownloadFile(mavenUrl, aarPath);

        if (File.Exists(aarPath))
        {
            // Clean up existing extracted directory
            if (Directory.Exists(extractedDir))
            {
                Directory.Delete(extractedDir, recursive: true);
            }

            // Extract the AAR file (it's essentially a ZIP file)
            ZipFile.ExtractToDirectory(aarPath, extractedDir);

            // Copy native libraries to the appropriate locations
            string jniLibsPath = Path.Combine(extractedDir, "jni");
            if (Directory.Exists(jniLibsPath))
            {
                // Copy each architecture's native libraries
                foreach (string archDir in Directory.GetDirectories(jniLibsPath))
                {
                    string archName = Path.GetFileName(archDir);
                    string targetArchDir = Path.Combine(nativeDir, archName);
                    Directory.CreateDirectory(targetArchDir);

                    foreach (string libFile in Directory.GetFiles(archDir, "*.so"))
                    {
                        string targetLibPath = Path.Combine(targetArchDir, Path.GetFileName(libFile));
                        File.Copy(libFile, targetLibPath, overwrite: true);
                    }
                }
            }

            // Clean up extracted directory and AAR file
            Directory.Delete(extractedDir, recursive: true);
            File.Delete(aarPath);

            Console.WriteLine($"AAR file extracted successfully from {aarFile} to native libraries in {nativeDir}");
        }
        else
        {
            throw new IOException($"File {aarFile} does not exist.");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error processing {aarFile}: {ex.Message}");
    }
	}

    static async Task MauiIosSetup()
    {
        string powersyncMauiPath = Path.Combine(AppContext.BaseDirectory, "../../../../..", "PowerSync/PowerSync.Maui/");

        string nativeDir = Path.Combine(powersyncMauiPath, "Platforms", "iOS", "NativeLibs");
        Directory.CreateDirectory(nativeDir);

        string extractedFramework = "powersync-sqlite-core.xcframework";
        string originalFile = "powersync-sqlite-core.xcframework.zip";
        string sqliteMauiPath = Path.Combine(nativeDir, originalFile);
        string extractedPath = Path.Combine(nativeDir, extractedFramework);

        try
        {
            await DownloadFile($"{BASE_URL}/{originalFile}", sqliteMauiPath);

            if (File.Exists(sqliteMauiPath))
            {
                // Extract the ZIP file
                if (Directory.Exists(extractedPath))
                {
                    Directory.Delete(extractedPath, recursive: true);
                }
        
                ZipFile.ExtractToDirectory(sqliteMauiPath, nativeDir);
        
                // Clean up the ZIP file
                File.Delete(sqliteMauiPath);
        
                Console.WriteLine($"File extracted successfully from {originalFile} to {extractedFramework} in {nativeDir}");
            }
            else
            {
                throw new IOException($"File {originalFile} does not exist.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error processing {originalFile}: {ex.Message}");
        }
    }
    
    static async Task DesktopSetup()
    {
        string powersyncCommonPath = Path.Combine(AppContext.BaseDirectory, "../../../../..", "PowerSync/PowerSync.Common/");
        
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
            string nativeDir = Path.Combine(powersyncCommonPath, "runtimes", rid, "native");
            Directory.CreateDirectory(nativeDir);

            string sqliteCommonPath = Path.Combine(nativeDir, originalFile);
            string newFilePath = Path.Combine(nativeDir, newFile);

            try
            {
                await DownloadFile($"{BASE_URL}/{originalFile}", sqliteCommonPath);

                if (File.Exists(sqliteCommonPath))
                {
                    File.Move(sqliteCommonPath, newFilePath, overwrite: true);
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