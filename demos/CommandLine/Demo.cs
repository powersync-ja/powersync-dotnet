namespace CommandLine;

using System.Reflection;

using CommandLine.Utils;

using PowerSync.Common.Client;
using PowerSync.Common.Client.Connection;

using Spectre.Console;

using Dapper;
using System.ComponentModel.DataAnnotations.Schema;

class Demo
{

    private class SpecialListResult
    {
        [Column("name")]
        public string? Name { get; set; }

        [Column("owner_id")]
        public string? OwnerId { get; set; }

        [Column("id")]
        public string? Id { get; set; }

        [Column("created_at")]
        public string? CreatedAt { get; set; }
    }

    private record ListResult(string id, string name, string owner_id, string created_at);
    static async Task Main()
    {
        var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "cli-example.db" },
            Schema = AppSchema.PowerSyncSchema,
        });
        await db.Init();

        var config = new Config();

        IPowerSyncBackendConnector connector;

        string connectorUserId = "";

        if (config.UseSupabase)
        {
            var supabaseConnector = new SupabaseConnector(config);

            // Ensure this user already exists
            await supabaseConnector.Login(config.SupabaseUsername, config.SupabasePassword);

            connectorUserId = supabaseConnector.UserId;

            connector = supabaseConnector;
        }
        else
        {
            var nodeConnector = new NodeConnector(config);

            connectorUserId = nodeConnector.UserId;

            connector = nodeConnector;
        }

        var table = new Table()
            .AddColumn("id")
            .AddColumn("name")
            .AddColumn("owner_id")
            .AddColumn("created_at");

        Console.WriteLine("Press ESC to exit.");
        Console.WriteLine("Press Enter to add a new row.");
        Console.WriteLine("Press Backspace to delete the last row.");
        Console.WriteLine("");

        bool running = true;

        await db.Watch("select * from lists", null, new WatchHandler<ListResult>
        {
            OnResult = (results) =>
            {
                table.Rows.Clear();
                foreach (var line in results)
                {
                    table.AddRow(line.id, line.name, line.owner_id, line.created_at);
                }

                var x = db.Database.GetWriteDatabaseConnection();
                var result = x.Query<SpecialListResult>("select * from lists");

                Console.WriteLine("DAPPER TIME");
                foreach (var line in result)
                {
                    Console.WriteLine($"DAPPER ROW: {line.Id} | {line.Name} | {line.OwnerId} | {line.CreatedAt}");
                }
            },
            OnError = (error) =>
            {
                Console.WriteLine("Error: " + error.Message);
            }
        });

        var _ = Task.Run(async () =>
         {
             while (running)
             {
                 if (Console.KeyAvailable)
                 {
                     var key = Console.ReadKey(intercept: true);
                     if (key.Key == ConsoleKey.Escape)
                     {
                         running = false;
                     }
                     else if (key.Key == ConsoleKey.Enter)
                     {
                         await db.Execute("insert into lists (id, name, owner_id, created_at) values (uuid(), 'New User', ?, datetime())", [connectorUserId]);
                     }
                     else if (key.Key == ConsoleKey.Backspace)
                     {
                         await db.Execute("delete from lists where id = (select id from lists order by created_at desc limit 1)");
                     }
                 }
                 Thread.Sleep(100);
             }
         });

        await db.Connect(connector, new PowerSync.Common.Client.Sync.Stream.PowerSyncConnectionOptions
        {
            AppMetadata = new Dictionary<string, string>
            {
                { "app_version", GetAppVersion() },
            }
        });
        await db.WaitForFirstSync();

        var panel = new Panel(table)
        {
            Header = new PanelHeader("")
        };
        var connected = false;

        db.RunListener((update) =>
        {
            if (update.StatusChanged != null)
            {
                connected = update.StatusChanged.Connected;
            }
        });

        // Start live updating table
        await AnsiConsole.Live(panel)
            .StartAsync(async ctx =>
            {
                while (running)
                {
                    panel.Header = new PanelHeader($"|    Connected: {connected}    |");
                    await Task.Delay(1000);
                    ctx.Refresh();

                }
            });

        Console.WriteLine("\nExited live table. Press any key to exit.");
    }

    private static string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version?.ToString() ?? "unknown";
    }
}
