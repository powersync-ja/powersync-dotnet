<p align="center">
  <a href="https://www.powersync.com" target="_blank"><img src="https://github.com/powersync-ja/.github/assets/7372448/d2538c43-c1a0-4c47-9a76-41462dba484f"/></a>
</p>

_[PowerSync](https://www.powersync.com) is a sync engine for building local-first apps with instantly-responsive UI/UX and simplified state transfer. Syncs between SQLite on the client-side and Postgres, MongoDB or MySQL on the server-side._

# PowerSync .NET SDKs

`powersync-dotnet` is the monorepo for PowerSync .NET SDKs.

## ⚠️ Project Status & Release Note

This package is part of a monorepo that is not yet officially released or published. It is currently in a pre-alpha state, intended strictly for closed testing. Expect breaking changes and instability as development continues.

Do not rely on this package for production use.

## Monorepo Structure: Packages

- [PowerSync/Common](./PowerSync/Common/README.md)

  - Core package: .NET implementation of a PowerSync database connector and streaming sync bucket implementation. Packages meant for specific platforms will extend functionality of `Common`.

## Demo Apps / Example Projects

Demo applications are located in the [`demos/`](./demos/) directory. Also see our [Demo Apps / Example Projects](https://docs.powersync.com/resources/demo-apps-example-projects) gallery which lists all projects by the backend and client-side framework they use.

### Command-Line

- [demos/CommandLine](./demos/CommandLine/README.md): A CLI to-do list example app using a Node-js backend.

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

