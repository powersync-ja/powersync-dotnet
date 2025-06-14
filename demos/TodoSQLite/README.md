# .NET MAUI ToDo List Demo App

## To test (we currently recommend testing on iOS):
1. You need to have one of our Node.js self-host demos ([Postgres](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs) | [MongoDB](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mongodb) | [MySQL](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mysql)) running, as it provides the PowerSync server that this demo's SDK connects to.
2. In the root directly run:
   1. `dotnet run --project Tools/Setup`
   2. `dotnet restore`
3. cd into this directory: `* cd demos/TodoSQLite `
4. run `dotnet build -t:Run -f:net8.0-ios`
   1. Or specify an iOS simulator identifier e.g.: `dotnet build -t:Run -f:net8.0-ios -p:_DeviceName=:v2:udid=B1CA156A-56FC-4C3C-B35D-4BC349111FDF`
5. Changes made to the backend's source DB (inspect via a tool like `psql`) or to the self-hosted aapp's web UI will be synced to this iOS client (and vice versa)

## Current known issues:
* Switching from offline to online mode isn't yet handled
* Android has some instability

