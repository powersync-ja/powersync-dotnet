# API Reference Docs

API documentation website for the PowerSync .NET SDK, generated via `docfx`.

## Building locally

1. Install `docfx`:

`docfx` is installed as a local .NET tool.

```bash
cd docs
dotnet tool restore
```

2. Build and serve on `http://localhost:8080`:

```bash
dotnet tool restore
dotnet docfx docfx.json --serve
```

## Publishing

- `.github/workflows/build-docs.yml`: Builds the site on every push.
- `.github/workflows/deploy-docs.yml`: Builds and publishes the site on push to `main`.

## Notes

Projects are built with only the `net8.0` target present (see the `properties` field in `docfx.json`) so that the MAUI workloads and the native `powersync-sqlite-core` binaries are not required to build the docs.
