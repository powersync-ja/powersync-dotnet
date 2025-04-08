# PowerSync CLI Demo App

This demo features a CLI-based table view that stays *live* using a *watch query*, ensuring the data updates in real time as changes occur.
To run this demo, you need to have one of our Node.js self-host demos ([Postgres](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs) | [MongoDB](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mongodb) | [MySQL](https://github.com/powersync-ja/self-host-demo/tree/main/demos/nodejs-mysql)) running, as it provides the PowerSync server that this CLI's PowerSync SDK connects to.

Changes made to the backend's source DB or to the self-hosted web UI will be synced to this CLI client (and vice versa).

## Authentication

This essentially uses anonymous authentication. A random user ID is generated and stored in local storage. The backend returns a valid token which is not linked to a specific user. All data is synced to all users.

## Connection Options

By default, this demo uses the NodeConnector for connecting to the PowerSync server. However, you can swap this out with the SupabaseConnector if needed:

1. Copy the `.env.template` file to a new `.env` file:
   ```bash
   # On Linux/macOS
   cp .env.template .env
   
   # On Windows
   copy .env.template .env
   ```

2. Replace the necessary fields in the `.env` file with your Supabase and PowerSync credentials:
   ```
   SUPABASE_URL=your_supabase_url
   SUPABASE_ANON_KEY=your_supabase_anon_key
   POWERSYNC_URL=your_powersync_url
   ```

3. Update your connector configuration to use SupabaseConnector instead of NodeConnector

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

To run the Command-Line interface:

```bash
dotnet run Demo
```