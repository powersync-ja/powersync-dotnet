# .NET MAUI ToDo List Demo App

This demo showcases using the [PowerSync .NET SDK](https://docs.powersync.com/client-sdk-references/dotnet) with .NET MAUI (Android, iOS or Windows).

## How to test:

To run this demo, you need to have one of our Node.js self-host demos ([Postgres](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs) | [MongoDB](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mongodb) | [MySQL](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mysql)) running, as it provides the PowerSync server that this demo's SDK connects to.

Changes made to the backend's source DB or to the self-hosted web UI will be synced to this client (and vice versa).

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