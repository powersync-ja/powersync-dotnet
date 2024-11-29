<p align="center">
  <a href="https://www.powersync.com" target="_blank"><img src="https://github.com/powersync-ja/.github/assets/7372448/d2538c43-c1a0-4c47-9a76-41462dba484f"/></a>
</p>

_[PowerSync](https://www.powersync.com) is a sync engine for building local-first apps with instantly-responsive UI/UX and simplified state transfer. Syncs between SQLite on the client-side and Postgres, MongoDB or MySQL on the server-side._

# PowerSync .NET SDKs

`powersync-dotnet` is the monorepo for PowerSync .NET SDKs.

## Monorepo Structure: Packages

- [PowerSync/Common](./PowerSync/Common/README.md)

  - Core package: .NET implementation of a PowerSync database connector and streaming sync bucket implementation. Packages meant for specific platforms will extend functionality of `Common`.

## Demo Apps / Example Projects

Demo applications are located in the [`demos/`](./demos/) directory. Also see our [Demo Apps / Example Projects](https://docs.powersync.com/resources/demo-apps-example-projects) gallery which lists all projects by the backend and client-side framework they use.

### Command-Line

- [demos/Command-Line/CLI](./demos/Command-Line/CLI/README.md): A CLI to-do list example app using a Node-js backend.

# Supported Frameworks

This monorepo currently targets the following .NET versions:
-	**.NET 8** ([primary target](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), recommended for all new projects)
- **.NET 6** (supported for compatibility with older projects)
-	**.NET Standard 2.0** (for compatibility with older libraries and frameworks)

We are also actively working towards adding support for **.NET Framework 4.8** to enable compatibility with legacy applications.

When running commands such as `dotnet run` or `dotnet test`, you may need to specify the target framework explicitly using the `--framework` flag.

# Development

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

