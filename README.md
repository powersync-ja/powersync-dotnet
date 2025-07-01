<p align="center">
  <a href="https://www.powersync.com" target="_blank"><img src="https://github.com/powersync-ja/.github/assets/7372448/d2538c43-c1a0-4c47-9a76-41462dba484f"/></a>
</p>

_[PowerSync](https://www.powersync.com) is a sync engine for building local-first apps with instantly-responsive UI/UX and simplified state transfer. Syncs between SQLite on the client-side and Postgres, MongoDB or MySQL on the server-side._

# PowerSync .NET SDKs

`powersync-dotnet` is the monorepo for PowerSync .NET SDKs.

## Monorepo Structure: Packages

Packages are published to [NuGet](https://www.nuget.org/profiles/PowerSync).

- [PowerSync/Common](./PowerSync/Common/README.md)

  - Core package: .NET implementation of a PowerSync database connector and streaming sync bucket implementation. Packages meant for specific platforms will extend functionality of `Common`.

## Demo Apps / Example Projects

Demo applications are located in the [`demos/`](./demos/) directory. Also see our [Demo Apps / Example Projects](https://docs.powersync.com/resources/demo-apps-example-projects) gallery which lists all projects by the backend and client-side framework they use.

- [demos/CommandLine](./demos/CommandLine/README.md): A CLI-based app demonstrating real-time data sync
- [demos/WPF](./demos/WPF/README.md): A Windows desktop to-do list app real-time data sync
- [demos/MAUITodo](./demos/MAUITodo/README.md): A cross-platform mobile and desktop to-do list app built with .NET MAUI, running on iOS, Android and Windows

# Supported Frameworks

This PowerSync SDK currently targets the following .NET versions:
- **.NET 9** - [Latest version](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
-	**.NET 8** - [Current LTS Version, used for development of this project](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- **.NET 6** - supported for compatibility with older projects)
-	**.NET Standard 2.0** - for compatibility with older libraries and frameworks, tested/verified older versions will be listed below.

- .NET Framework 4.8:
    
    To get a .NET Framework 4.8 working with this SDK add the following to your `.csproj` file:

    ```xml
    <PropertyGroup>
      ...
      <!-- Ensures the correct SQLite DLL is available -->
      <RuntimeIdentifiers>win-x86;win-x64</RuntimeIdentifiers>
      <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    </PropertyGroup>

    <ItemGroup>
      ...
      <!-- Ensures the HTTP client resolves in the SDK -->
      <PackageReference Include="System.Net.Http" Version="4.3.4" /> 
    </ItemGroup>
    ```

    and create a `IsExternalInit.cs` file in your project with the following contents:
    
    ```cs
    using System.ComponentModel;

    namespace System.Runtime.CompilerServices
    {
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal class IsExternalInit { }
    }
    ``` 
    
------- 

When running commands such as `dotnet run` or `dotnet test`, you may need to specify the target framework explicitly using the `--framework` flag.

# Development

Download PowerSync extension

```bash
dotnet run --project Tools/Setup    
```

Install dependencies

```bash
dotnet restore
```

## Tests

Run all tests

```bash
dotnet test -v n --framework net8.0
```

Run a specific test

```bash
dotnet test -v n --framework net8.0 --filter "test-file-pattern"  
```

## Using the PowerSync.Common package in your project
```bash
dotnet add package PowerSync.Common --prerelease
```