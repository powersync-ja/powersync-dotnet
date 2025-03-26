namespace PowerSync.Common.Utils;

using System.Runtime.InteropServices;

public static class PowerSyncPathResolver
{
    public static string GetNativeLibraryPath(string packagePath)
    {
        string rid = GetRuntimeIdentifier();
        string nativeDir = Path.Combine(packagePath, "runtimes", rid, "native");

        string fileName = GetFileNameForPlatform();

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