namespace PowerSync.Common.PerformanceTests;

using System.Diagnostics;

using Newtonsoft.Json;

using PowerSync.Common.Utils;

[Trait("Category", "Performance")]
public class DeepEqualityPerformanceTests
{
    [PerformanceFact]
    public void Performance_JsonSerialize_vs_DeepEqual_1000Iterations()
    {
        const int iterations = 100000;

        var parameters1 = new Dictionary<string, object>
        {
            ["userId"] = "user-123",
            ["organizationId"] = "org-456",
            ["includeArchived"] = false,
            ["limit"] = 100,
            ["tags"] = new List<string> { "active", "verified" }
        };

        var parameters2 = new Dictionary<string, object>
        {
            ["userId"] = "user-123",
            ["organizationId"] = "org-456",
            ["includeArchived"] = false,
            ["limit"] = 100,
            ["tags"] = new List<string> { "active", "verified" }
        };

        for (int i = 0; i < 10; i++)
        {
            _ = JsonConvert.SerializeObject(parameters1) == JsonConvert.SerializeObject(parameters2);
            Equality.DeepEquals(parameters1, parameters2);
        }

        var jsonSw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var json1 = JsonConvert.SerializeObject(parameters1);
            var json2 = JsonConvert.SerializeObject(parameters2);
            _ = json1 == json2;
        }
        jsonSw.Stop();

        var deepEqualSw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            Equality.DeepEquals(parameters1, parameters2);
        }
        deepEqualSw.Stop();

        Console.WriteLine($"Performance Results ({iterations} iterations):");
        Console.WriteLine($"JSON Serialize + Compare: {jsonSw.ElapsedMilliseconds}ms ({jsonSw.ElapsedTicks} ticks)");
        Console.WriteLine($"DeepEqual:                {deepEqualSw.ElapsedMilliseconds}ms ({deepEqualSw.ElapsedTicks} ticks)");
        Console.WriteLine($"Ratio (DeepEqual/JSON):   {(double)deepEqualSw.ElapsedTicks / jsonSw.ElapsedTicks:F2}x");
    }

    [PerformanceFact]
    public void Performance_JsonSerialize_vs_DeepEqual_WithNestedObjects()
    {
        const int iterations = 100000;

        var parameters1 = new Dictionary<string, object>
        {
            ["user"] = new Dictionary<string, object>
            {
                ["id"] = "user-123",
                ["profile"] = new Dictionary<string, object>
                {
                    ["name"] = "John Doe",
                    ["email"] = "john@example.com",
                    ["settings"] = new Dictionary<string, object>
                    {
                        ["theme"] = "dark",
                        ["notifications"] = true
                    }
                }
            },
            ["filters"] = new List<object>
            {
                new Dictionary<string, object> { ["field"] = "status", ["value"] = "active" },
                new Dictionary<string, object> { ["field"] = "type", ["value"] = "premium" }
            }
        };

        var parameters2 = new Dictionary<string, object>
        {
            ["user"] = new Dictionary<string, object>
            {
                ["id"] = "user-123",
                ["profile"] = new Dictionary<string, object>
                {
                    ["name"] = "John Doe",
                    ["email"] = "john@example.com",
                    ["settings"] = new Dictionary<string, object>
                    {
                        ["theme"] = "dark",
                        ["notifications"] = true
                    }
                }
            },
            ["filters"] = new List<object>
            {
                new Dictionary<string, object> { ["field"] = "status", ["value"] = "active" },
                new Dictionary<string, object> { ["field"] = "type", ["value"] = "premium" }
            }
        };

        for (int i = 0; i < 10; i++)
        {
            _ = JsonConvert.SerializeObject(parameters1) == JsonConvert.SerializeObject(parameters2);
            Equality.DeepEquals(parameters1, parameters2);
        }

        var jsonSw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var json1 = JsonConvert.SerializeObject(parameters1);
            var json2 = JsonConvert.SerializeObject(parameters2);
            _ = json1 == json2;
        }
        jsonSw.Stop();

        var deepEqualSw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            Equality.DeepEquals(parameters1, parameters2);
        }
        deepEqualSw.Stop();

        Console.WriteLine($"Performance Results - Nested Objects ({iterations} iterations):");
        Console.WriteLine($"JSON Serialize + Compare: {jsonSw.ElapsedMilliseconds}ms ({jsonSw.ElapsedTicks} ticks)");
        Console.WriteLine($"DeepEqual:                {deepEqualSw.ElapsedMilliseconds}ms ({deepEqualSw.ElapsedTicks} ticks)");
        Console.WriteLine($"Ratio (DeepEqual/JSON):   {(double)deepEqualSw.ElapsedTicks / jsonSw.ElapsedTicks:F2}x");
    }

    [PerformanceFact]
    public void Performance_JsonSerialize_vs_DeepEqual_NotEqual()
    {
        const int iterations = 100000;

        var parameters1 = new Dictionary<string, object>
        {
            ["userId"] = "user-123",
            ["organizationId"] = "org-456",
            ["includeArchived"] = false,
            ["limit"] = 100
        };

        var parameters2 = new Dictionary<string, object>
        {
            ["userId"] = "user-999",
            ["organizationId"] = "org-456",
            ["includeArchived"] = false,
            ["limit"] = 100
        };

        for (int i = 0; i < 10; i++)
        {
            _ = JsonConvert.SerializeObject(parameters1) == JsonConvert.SerializeObject(parameters2);
            Equality.DeepEquals(parameters1, parameters2);
        }

        var jsonSw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            var json1 = JsonConvert.SerializeObject(parameters1);
            var json2 = JsonConvert.SerializeObject(parameters2);
            _ = json1 == json2;
        }
        jsonSw.Stop();

        var deepEqualSw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            Equality.DeepEquals(parameters1, parameters2);
        }
        deepEqualSw.Stop();

        Console.WriteLine($"Performance Results - Not Equal ({iterations} iterations):");
        Console.WriteLine($"JSON Serialize + Compare: {jsonSw.ElapsedMilliseconds}ms ({jsonSw.ElapsedTicks} ticks)");
        Console.WriteLine($"DeepEqual:                {deepEqualSw.ElapsedMilliseconds}ms ({deepEqualSw.ElapsedTicks} ticks)");
        Console.WriteLine($"Ratio (DeepEqual/JSON):   {(double)deepEqualSw.ElapsedTicks / jsonSw.ElapsedTicks:F2}x");
    }
}


[Trait("Category", "Performance")]
public class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_PERFORMANCE_TESTS") != "true")
        {
            Skip = "Performance tests are disabled. Set RUN_PERFORMANCE_TESTS=true to run.";
        }

    }
}
