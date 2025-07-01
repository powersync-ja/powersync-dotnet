# .NET MAUI ToDo List Demo App

## Quickstart for testing  (we currently recommend testing on iOS):
1. You need to have one of our Node.js self-host demos ([Postgres](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs) | [MongoDB](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mongodb) | [MySQL](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mysql)) running, as it provides the PowerSync server that this demo's SDK connects to.
2. In the root directly run:
   1. `dotnet run --project Tools/Setup`
   2. `dotnet restore`
3. cd into this directory: `* cd demos/MAUITodo `
4. run `dotnet build -t:Run -f:net8.0-ios`
   1. Or specify an iOS simulator identifier e.g.: `dotnet build -t:Run -f:net8.0-ios -p:_DeviceName=:v2:udid=B1CA156A-56FC-4C3C-B35D-4BC349111FDF`
5. Changes made to the backend's source DB (inspect via a tool like `psql`) or to the self-hosted aapp's web UI will be synced to this iOS client (and vice versa)


## Getting Started

In the repo root, run the following to download the PowerSync extension:

```bash
dotnet run --project Tools/Setup    
```

Then switch into the demo's directory:

Install dependencies:

```bash
dotnet restore
```

## Running the App

### iOS

```sh
dotnet build -t:Run -f:net8.0-ios
```

Specifyng an iOS simulator
```sh
dotnet build -t:Run -f:net8.0-ios -p:_DeviceName=:v2:udid=B1CA156A-56FC-4C3C-B35D-4BC349111FDF
```

### Android

```sh
dotnet build -t:Run -f:net8.0-android
```

Specifying an Android emulator
```sh
dotnet build -t:Run -f:net8.0-android -p:_DeviceName=emulator-5554
```

### Windows

```sh
dotnet run -f net8.0-windows10.0.19041.0
```
