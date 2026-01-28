namespace PowerSync.Common.Tests;

using System.Diagnostics;

using Newtonsoft.Json;

using PowerSync.Common.Utils;

public class DeepEqualityTests
{
    [Fact]
    public void Integers_Equal_ShouldPass()
    {
        int a = 42;
        int b = 42;

        Assert.True(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void Integers_NotEqual_ShouldFail()
    {
        int a = 42;
        int b = 43;

        Assert.False(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void Strings_Equal_ShouldPass()
    {
        string a = "hello";
        string b = "hello";

        Assert.True(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void Strings_NotEqual_ShouldFail()
    {
        string a = "hello";
        string b = "world";

        Assert.False(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void Booleans_Equal_ShouldPass()
    {
        bool a = true;
        bool b = true;

        Assert.True(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void Doubles_Equal_ShouldPass()
    {
        double a = 3.14159;
        double b = 3.14159;

        Assert.True(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void NullValues_BothNull_ShouldPass()
    {
        string? a = null;
        string? b = null;

        Assert.True(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void NullVsValue_ShouldFail()
    {
        string? a = null;
        string b = "value";

        Assert.False(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void Dictionary_vs_List_ShouldFail()
    {
        var dict = new Dictionary<string, object> { ["a"] = 1 };
        var list = new List<object> { 1 };

        Assert.False(Equality.DeepEquals(dict, list));
    }

    [Fact]
    public void Int_vs_Long_ShouldFail()
    {
        object a = 42;
        object b = 42L;

        Assert.False(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void Int_vs_Double_ShouldFail()
    {
        object a = 42;
        object b = 42.0;

        Assert.False(Equality.DeepEquals(a, b));
    }

    [Fact]
    public void Dictionary_BothEmpty_ShouldPass()
    {
        var dict1 = new Dictionary<string, object>();
        var dict2 = new Dictionary<string, object>();

        Assert.True(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void List_BothEmpty_ShouldPass()
    {
        var list1 = new List<object>();
        var list2 = new List<object>();

        Assert.True(Equality.DeepEquals(list1, list2));
    }

    [Fact]
    public void SameReference_Dictionary_ShouldPass()
    {
        var dict = new Dictionary<string, object> { ["a"] = 1 };

        Assert.True(Equality.DeepEquals(dict, dict));
    }

    [Fact]
    public void SameReference_List_ShouldPass()
    {
        var list = new List<object> { 1, 2, 3 };

        Assert.True(Equality.DeepEquals(list, list));
    }

    [Fact]
    public void Dictionary_SameKeyValues_ShouldPass()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["name"] = "John",
            ["age"] = 30
        };

        var dict2 = new Dictionary<string, object>
        {
            ["name"] = "John",
            ["age"] = 30
        };

        Assert.True(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_DifferentOrder_ShouldPass()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["name"] = "John",
            ["age"] = 30
        };

        var dict2 = new Dictionary<string, object>
        {
            ["age"] = 30,
            ["name"] = "John"
        };

        Assert.True(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_DifferentValues_ShouldFail()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["name"] = "John",
            ["age"] = 30
        };

        var dict2 = new Dictionary<string, object>
        {
            ["name"] = "Jane",
            ["age"] = 30
        };

        Assert.False(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_DifferentKeys_ShouldFail()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["name"] = "John"
        };

        var dict2 = new Dictionary<string, object>
        {
            ["fullName"] = "John"
        };

        Assert.False(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_ExtraKey_ShouldFail()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["name"] = "John"
        };

        var dict2 = new Dictionary<string, object>
        {
            ["name"] = "John",
            ["age"] = 30
        };

        Assert.False(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_NestedDictionary_ShouldPass()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["user"] = new Dictionary<string, object>
            {
                ["name"] = "John",
                ["email"] = "john@example.com"
            }
        };

        var dict2 = new Dictionary<string, object>
        {
            ["user"] = new Dictionary<string, object>
            {
                ["name"] = "John",
                ["email"] = "john@example.com"
            }
        };

        Assert.True(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_WithListValues_ShouldPass()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["tags"] = new List<string> { "red", "green", "blue" }
        };

        var dict2 = new Dictionary<string, object>
        {
            ["tags"] = new List<string> { "red", "green", "blue" }
        };

        Assert.True(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_WithListValues_DifferentOrder_ShouldFail()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["tags"] = new List<string> { "red", "green", "blue" }
        };

        var dict2 = new Dictionary<string, object>
        {
            ["tags"] = new List<string> { "blue", "green", "red" }
        };

        Assert.False(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_WithNullValue_ShouldPass()
    {
        var dict1 = new Dictionary<string, object?>
        {
            ["name"] = "John",
            ["nickname"] = null
        };

        var dict2 = new Dictionary<string, object?>
        {
            ["name"] = "John",
            ["nickname"] = null
        };

        Assert.True(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_WithNullValue_DifferentTypes_ShouldPass()
    {
        var dict1 = new Dictionary<string, object?> { ["a"] = null };
        var dict2 = new Dictionary<string, object?> { ["a"] = null };

        Assert.True(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void Dictionary_MixedTypes_ShouldPass()
    {
        var dict1 = new Dictionary<string, object>
        {
            ["stringValue"] = "hello",
            ["intValue"] = 42,
            ["boolValue"] = true,
            ["doubleValue"] = 3.14
        };

        var dict2 = new Dictionary<string, object>
        {
            ["stringValue"] = "hello",
            ["intValue"] = 42,
            ["boolValue"] = true,
            ["doubleValue"] = 3.14
        };

        Assert.True(Equality.DeepEquals(dict1, dict2));
    }

    [Fact]
    public void List_NestedLists_ShouldPass()
    {
        var list1 = new List<object> { new List<object> { 1, 2 }, new List<object> { 3, 4 } };
        var list2 = new List<object> { new List<object> { 1, 2 }, new List<object> { 3, 4 } };

        Assert.True(Equality.DeepEquals(list1, list2));
    }

    [Fact]
    public void List_NestedLists_DifferentValues_ShouldFail()
    {
        var list1 = new List<object> { new List<object> { 1, 2 }, new List<object> { 3, 4 } };
        var list2 = new List<object> { new List<object> { 1, 2 }, new List<object> { 3, 5 } };

        Assert.False(Equality.DeepEquals(list1, list2));
    }

    [Fact]
    public void List_WithDictionaries_ShouldPass()
    {
        var list1 = new List<object>
        {
            new Dictionary<string, object> { ["id"] = 1, ["name"] = "first" },
            new Dictionary<string, object> { ["id"] = 2, ["name"] = "second" }
        };

        var list2 = new List<object>
        {
            new Dictionary<string, object> { ["id"] = 1, ["name"] = "first" },
            new Dictionary<string, object> { ["id"] = 2, ["name"] = "second" }
        };

        Assert.True(Equality.DeepEquals(list1, list2));
    }

    [Fact]
    public void List_DifferentLengths_ShouldFail()
    {
        var list1 = new List<object> { 1, 2, 3 };
        var list2 = new List<object> { 1, 2 };

        Assert.False(Equality.DeepEquals(list1, list2));
    }

    [Fact]
    public void List_WithNullElements_ShouldPass()
    {
        var list1 = new List<object?> { 1, null, 3 };
        var list2 = new List<object?> { 1, null, 3 };

        Assert.True(Equality.DeepEquals(list1, list2));
    }

    [Fact]
    public void Dictionary_ComplexNestedStructures_ShouldPass()
    {
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

        Assert.True(Equality.DeepEquals(parameters1, parameters2));
    }

    [Fact]
    public void Dictionary_ComplexNestedStructures_ShouldFail()
    {
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
                        ["notifications"] = false // differs
                    }
                }
            },
            ["filters"] = new List<object>
            {
                new Dictionary<string, object> { ["field"] = "status", ["value"] = "active" },
                new Dictionary<string, object> { ["field"] = "type", ["value"] = "premium" }
            }
        };

        Assert.False(Equality.DeepEquals(parameters1, parameters2));
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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
