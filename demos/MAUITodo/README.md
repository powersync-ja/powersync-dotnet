# .NET MAUI ToDo List Demo App

This demo showcases using the [PowerSync .NET SDK](https://docs.powersync.com/client-sdk-references/dotnet) with .NET MAUI (Android, iOS or Windows).

## How to test:

To run this demo, you need to have one of our Node.js self-host demos ([Postgres](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs) | [MongoDB](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mongodb) | [MySQL](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mysql)) running, as it provides the PowerSync server that this demo's SDK connects to.

Changes made to the backend's source DB or to the self-hosted web UI will be synced to this client (and vice versa).

## Pull-to-refresh with explicit checkpoints

The lists and todos screens support pull-to-refresh. Swiping down asks the PowerSync service for a
checkpoint via `PowerSyncDatabase.RequestCheckpoint()` and waits for the local database to apply
everything up to it with `CheckpointRequest.WaitForSync()`, so the spinner only stops once the local
view has actually caught up to the service.

This requires connecting with `CheckpointMode.Requests()` (see `Data/PowerSyncData.cs`) and
**PowerSync service version 1.24.0 or later**. Against an older service the refresh will report a
sync error instead of completing. Checkpoint requests are currently an alpha API.

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

### MacCatalyst

```sh
dotnet build -t:Run -f:net8.0-maccatalyst
```
