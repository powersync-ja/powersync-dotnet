namespace PowerSync.Common.Utils;

using System.Runtime.InteropServices;

public static class PowerSyncPathResolver
{
    public static string GetNativeLibraryPath(string packagePath)
    {
        string fileName = GetFileNameForPlatform();

        // Check the flattened path first since some technologies (eg. .NET 4.8 Framework) flatten libraries into the root folder.
        // Checking this path first also makes debugging easier, since one can easily change the resolved DLL.
        string flattenedPath = Path.Combine(packagePath, fileName);
        if (File.Exists(flattenedPath)) return flattenedPath;

        // Otherwise, check the native code dir
        string rid = GetRuntimeIdentifier();
        string nativeDir = Path.Combine(packagePath, "runtimes", rid, "native");

        return Path.Combine(nativeDir, fileName);
    }

    private static string GetRuntimeIdentifier()
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

    private static string GetFileNameForPlatform()
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
}
