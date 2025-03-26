namespace CommandLine;

using PowerSync.Common.Client;
using Spectre.Console;

class Demo
{

    private record ListResult(string id, string name, string owner_id, string created_at);
    static async Task Main()
    {
        var db = new PowerSyncDatabase(new PowerSyncDatabaseOptions
        {
            Database = new SQLOpenOptions { DbFilename = "cli-example.db" },
            Schema = AppSchema.PowerSyncSchema,
        });
        await db.Init();

        var connector = new NodeConnector();

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
                         await db.Execute("insert into lists (id, name, owner_id, created_at) values (uuid(), 'New User', ?, datetime())", [connector.UserId]);
                     }
                     else if (key.Key == ConsoleKey.Backspace)
                     {
                         await db.Execute("delete from lists where id = (select id from lists order by created_at desc limit 1)");
                     }
                 }
                 Thread.Sleep(100);
             }
         });

        await db.Connect(connector);
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
}