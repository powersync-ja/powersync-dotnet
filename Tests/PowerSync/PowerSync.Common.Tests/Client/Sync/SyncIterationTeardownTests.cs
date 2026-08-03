namespace PowerSync.Common.Tests.Client.Sync;

using System.Collections.Concurrent;

using PowerSync.Common.Client;
using PowerSync.Common.Tests.Utils;
using PowerSync.Common.Tests.Utils.Sync;

/// <summary>
/// Verifies how a sync iteration tears down: once an iteration has ended, no
/// further control op is forwarded to the core. The core rejects control ops
/// after its iteration stops ("No iteration is active"), so forwarding a queued
/// op after teardown would surface an unhandled exception. The control loop must
/// therefore stop forwarding as soon as the iteration closes.
///
/// This hooks the process-wide <see cref="TaskScheduler.UnobservedTaskException"/>,
/// so it filters on the core's error message rather than asserting the bag is
/// empty - other collections run in parallel and would otherwise bleed in.
///
/// dotnet test -v n --framework net8.0 --filter "SyncIterationTeardownTests"
/// </summary>
[Collection("SyncIterationTeardownTests")]
public class SyncIterationTeardownTests
{

    /// <summary>
    /// Disconnecting while sync lines are still queued must end the iteration
    /// cleanly, queued control ops are dropped rather than forwarded to a core
    /// that no longer has an active iteration.
    /// </summary>
    [Fact(Timeout = 60000)]
    public async Task DisconnectWithQueuedLinesEndsCleanly()
    {
        var unobserved = new ConcurrentBag<string>();
        void Handler(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            foreach (var ex in e.Exception.Flatten().InnerExceptions)
            {
                unobserved.Add(ex.Message);
            }
            e.SetObserved();
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            for (int i = 0; i < 12; i++)
            {
                var syncService = new MockSyncService();
                var db = syncService.CreateDatabase();
                await db.Init();

                await db.Connect(new TestConnector());

                // Establish + rapidly enqueue lines so the control queue backs up.
                syncService.PushLine(MockDataFactory.Checkpoint(lastOpId: 0, buckets: []));
                for (int j = 0; j < 30; j++)
                {
                    syncService.PushLine(MockDataFactory.Checkpoint(lastOpId: j, buckets: []));
                }

                // Disconnect while lines are still queued (e.g. socket death / reconnect).
                await db.Disconnect();

                syncService.Close();
                await db.Close();
                DatabaseUtils.CleanDb(db.Database.Name);
            }

            // Surface any unobserved task exceptions.
            for (int k = 0; k < 5; k++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(50);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }

        var leaked = unobserved.Where(m => m.Contains("No iteration is active")).ToList();
        Assert.True(leaked.Count == 0,
            $"Expected no control ops forwarded after teardown, but {leaked.Count} 'No iteration is active' exceptions escaped.");
    }
}
