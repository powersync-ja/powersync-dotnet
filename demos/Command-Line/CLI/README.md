# PowerSync CLI demo app

This demo features a CLI-based table view that stays *live* using a *watch query*, ensuring the data updates in real time as changes occur.
To run this demo, you need to have the [Node.js self-host demo](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs) running, as it provides the PowerSync server that this CLI's PowerSync SDK connects to.

Changes made to the backend's source DB or to the selfhosted web UI will be synced to this CLI client (and vice versa).

## Authentication

This essentially uses anonymous authentication. A random user ID is generated and stored in local storage. The backend returns a valid token which is not linked to a specific user. All data is synced to all users.

## Getting Started

Install dependencies

```bash
dotnet restore
```

To run the CLI

```bash
dotnet run Demo
```