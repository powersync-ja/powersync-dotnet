﻿using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CLI;
using Common.MicrosoftDataSqlite;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Node.Tests;
using Spectre.Console;

class Demo
{

    private record ListResult(string id, string name, string owner_id, string created_at);
    static async Task Main()
    {

        var db = CommonPowerSyncDatabase.Create(AppSchema.PowerSyncSchema, "CLI.db", createLogger());
        await db.Init();
        // await db.DisconnectAndClear();
        var connector = new NodeConnector();

        var table = new Table()
            .AddColumn("id")
            .AddColumn("name")
            .AddColumn("owner_id")
            .AddColumn("created_at");
        await db.Connect(connector);
        await Task.Delay(2000);

        var query = "select * from lists";

        Console.WriteLine("Press ESC to exit.");
        Console.WriteLine("Press Enter to add a new row.");
        Console.WriteLine("Press Backspace to delete the last row.");

        bool running = true;

        db.Watch(query, null, new Common.Client.WatchHandler
        {
            OnResult = async (results) =>
            {
                // Convert Object[] to JSON string// typing should be resolved somewhere else..
                var jsonString = JsonConvert.SerializeObject(results);
                var typedResults = JsonConvert.DeserializeObject<List<ListResult>>(jsonString);

                // Console.WriteLine(JsonConvert.SerializeObject(typedResults, Formatting.Indented));

                table.Rows.Clear();
                foreach (var line in typedResults!)
                {
                    table.AddRow(line.id, line.name, line.owner_id, line.created_at);
                }
            }
        });

        // Task to listen for key press (Stop when ESC is pressed)
        var _ = Task.Run(async () =>
         {
             while (running)
             {
                 if (Console.KeyAvailable)
                 {
                     var key = Console.ReadKey(intercept: true);
                     if (key.Key == ConsoleKey.Escape)
                     {
                         running = false; // Stop the loop
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
                 Thread.Sleep(100); // Avoid high CPU usage
             }
         });

        // Start live updating table
        await AnsiConsole.Live(table)
            .StartAsync(async ctx =>
            {
                int i = 0;
                while (running)
                {
                    await Task.Delay(1000);
                    ctx.Refresh();
                }
            });






        Console.WriteLine("\nExited live table. Press any key to exit.");
    }

    private static ILogger createLogger()
    {
        ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(); // Enable console logging
            builder.SetMinimumLevel(LogLevel.Information);
        });

        return loggerFactory.CreateLogger("TestLogger");
    }
}