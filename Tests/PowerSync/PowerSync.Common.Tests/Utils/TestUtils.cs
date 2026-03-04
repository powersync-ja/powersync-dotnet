using System.Runtime.CompilerServices;
using PowerSync.Common.Client;

namespace PowerSync.Common.Tests.Utils;

public static class TestUtils
{
    /// <summary>
    /// Deep equivalence assertion with line number on failure.
    /// </summary>
    public static void DeepEquivalent(object? expected, object? actual, [CallerLineNumber] int lineNumber = 0)
    {
        try
        {
            Assert.Equivalent(expected, actual, strict: true);
        }
        catch (Exception ex)
        {
            throw new Exception($"Equivalence assertion failed at line {lineNumber}: {ex.Message}", ex);
        }
    }

    public static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition not met within timeout");
    }

    public static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition not met within timeout");
    }

    public static async Task<string> InsertRandomAsset(PowerSyncDatabase db)
    {
        var id = Guid.NewGuid().ToString();
        await db.Execute(
            "insert into assets(id, description, make) values (?, ?, ?)",
            [id, "some desc", "some make"]
        );
        return id;
    }

    public static async Task<string[]> InsertRandomAssets(PowerSyncDatabase db, int assetCount)
    {
        var ids = Enumerable
            .Range(0, assetCount)
            .Select(_ => Guid.NewGuid().ToString())
            .ToArray();
        var parameters = ids
            .Select<string, object?[]>(id => [id, "some desc", "some make"])
            .ToArray();
        await db.ExecuteBatch(
            "insert into assets(id, description, make) values (?, ?, ?)",
            parameters
        );
        return ids;
    }
}
