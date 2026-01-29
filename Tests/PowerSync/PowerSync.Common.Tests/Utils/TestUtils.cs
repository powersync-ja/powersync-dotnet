using System.Runtime.CompilerServices;

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
}
