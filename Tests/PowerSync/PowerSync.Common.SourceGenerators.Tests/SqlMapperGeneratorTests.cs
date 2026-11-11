namespace PowerSync.Common.SourceGenerators.Tests;

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using PowerSync.Common.DB;

/// <summary>
/// dotnet test -v n --framework net10.0 --filter "SqlMapperGeneratorTests"
/// </summary>
public class SqlMapperGeneratorTests
{
    private record GeneratorRun(
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        ImmutableArray<Diagnostic> CompilationErrors,
        ImmutableArray<string> GeneratedSources);

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private static GeneratorRun Run(string source, bool referencePowerSync = true)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !Path.GetFileName(path).StartsWith("PowerSync.Common"))
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
        if (referencePowerSync)
        {
            references.Add(MetadataReference.CreateFromFile(typeof(IFromRow<>).Assembly.Location));
        }

        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new SqlMapperGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        var runResult = driver.GetRunResult();
        return new GeneratorRun(
            diagnostics,
            output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray(),
            runResult.GeneratedTrees.Select(t => t.ToString()).ToImmutableArray());
    }

    private static void AssertCompiles(GeneratorRun run)
    {
        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.CompilationErrors);
    }

    [Fact]
    public void GeneratesForClassesRecordsAndStructs()
    {
        var run = Run("""
            using PowerSync.Common.DB;
            using PowerSync.Common.DB.Schema.Attributes;

            namespace App;

            [GenerateSqlMapper]
            public partial class Todo
            {
                [Column("id")] public string TodoId { get; set; } = "";
                public string? Description { get; set; }
                public int Count { get; init; }
                [Ignored] public object? Extra { get; set; }
            }

            [GenerateSqlMapper]
            public partial record Summary(string Id, [property: Column("todo_count")] long TodoCount);

            [GenerateSqlMapper]
            public readonly partial record struct Point(double X, double Y);

            [GenerateSqlMapper]
            internal partial struct Plain
            {
                public long Value { get; set; }
            }

            public static class Usage
            {
                public static Todo Map(IQueryRow row) => Read<Todo>(row);
                public static Summary MapSummary(IQueryRow row) => Read<Summary>(row);
                public static Point MapPoint(IQueryRow row) => Read<Point>(row);
                internal static Plain MapPlain(IQueryRow row) => Read<Plain>(row);
                private static T Read<T>(IQueryRow row) where T : IFromRow<T> => T.FromRow(row);
            }
            """);

        AssertCompiles(run);
        var summary = Assert.Single(run.GeneratedSources, s => s.Contains("partial record Summary"));
        Assert.Contains("row.GetString(\"Id\")", summary);
        Assert.Contains("row.GetLong(\"todo_count\")", summary);
        var todo = Assert.Single(run.GeneratedSources, s => s.Contains("partial class Todo"));
        Assert.Contains("TodoId = row.GetString(\"id\")", todo);
        Assert.Contains("Description = row.GetStringOrNull(\"Description\")", todo);
        Assert.DoesNotContain("Extra", todo);
    }

    [Fact]
    public void GeneratesForNestedGenericAndGlobalNamespaceTypes()
    {
        var run = Run("""
            using PowerSync.Common.DB;

            [GenerateSqlMapper]
            public partial class Global
            {
                public string @class { get; set; } = "";
            }

            namespace App
            {
                public static partial class Outer
                {
                    [GenerateSqlMapper]
                    public partial class Inner<T>
                    {
                        public string Id { get; set; } = "";
                    }
                }
            }
            """);

        AssertCompiles(run);
        Assert.Contains(run.GeneratedSources, s => s.Contains("@class = row.GetString(\"class\")"));
    }

    [Fact]
    public void ReportsNonPartialType()
    {
        var run = Run("""
            using PowerSync.Common.DB;

            [GenerateSqlMapper]
            public class Todo
            {
                public string Id { get; set; } = "";
            }
            """);

        Assert.Equal("PSSG002", Assert.Single(run.GeneratorDiagnostics).Id);
    }

    [Fact]
    public void ReportsNonPartialContainingType()
    {
        var run = Run("""
            using PowerSync.Common.DB;

            public class Outer
            {
                [GenerateSqlMapper]
                public partial class Todo
                {
                    public string Id { get; set; } = "";
                }
            }
            """);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("PSSG002", diagnostic.Id);
        Assert.Contains("Outer", diagnostic.GetMessage());
    }

    [Fact]
    public void ReportsAbstractType()
    {
        var run = Run("""
            using PowerSync.Common.DB;

            [GenerateSqlMapper]
            public abstract partial class Todo
            {
                public string Id { get; set; } = "";
            }
            """);

        Assert.Equal("PSSG003", Assert.Single(run.GeneratorDiagnostics).Id);
    }

    [Fact]
    public void ReportsUnsupportedMemberTypes()
    {
        var run = Run("""
            using System.Collections.Generic;
            using PowerSync.Common.DB;

            [GenerateSqlMapper]
            public partial class Todo
            {
                public string Id { get; set; } = "";
                public List<string> Tags { get; set; } = new();
                public System.TimeSpan Duration { get; set; }
            }
            """);

        Assert.Equal(["PSSG004", "PSSG004"], run.GeneratorDiagnostics.Select(d => d.Id));
        Assert.Contains("Tags", run.GeneratorDiagnostics[0].GetMessage());
        Assert.Contains("Duration", run.GeneratorDiagnostics[1].GetMessage());
    }

    [Fact]
    public void ReportsMissingPowerSyncReference()
    {
        var run = Run("""
            namespace PowerSync.Common.DB
            {
                [GenerateSqlMapper]
                public partial class Todo
                {
                    public string Id { get; set; } = "";
                }
            }
            """, referencePowerSync: false);

        Assert.Equal("PSSG001", Assert.Single(run.GeneratorDiagnostics).Id);
    }

    [Fact]
    public void SkipsReadOnlyAndPrivateProperties()
    {
        var run = Run("""
            using PowerSync.Common.DB;

            [GenerateSqlMapper]
            public partial class Todo
            {
                public string Id { get; set; } = "";
                public string Upper => Id.ToUpperInvariant();
                private string Secret { get; set; } = "";
                public string Name { get; private set; } = "";
            }
            """);

        AssertCompiles(run);
        var source = Assert.Single(run.GeneratedSources, s => s.Contains("partial class Todo"));
        Assert.DoesNotContain("Upper", source);
        Assert.DoesNotContain("Secret", source);
        Assert.Contains("Name = row.GetString(\"Name\")", source);
    }

    [Fact]
    public void AttributeDoesNotConflictAcrossInternalsVisibleTo()
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path => !Path.GetFileName(path).StartsWith("PowerSync.Common"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(IFromRow<>).Assembly.Location))
            .ToList();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

        Compilation Generate(string name, string source, IEnumerable<MetadataReference> refs)
        {
            var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, ParseOptions)], refs, options);
            GeneratorDriver driver = CSharpGeneratorDriver.Create([new SqlMapperGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            Assert.Empty(diagnostics);
            return output;
        }

        var app = Generate("App", """
            using PowerSync.Common.DB;

            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("App.Tests")]

            [GenerateSqlMapper]
            public partial class Todo
            {
                public string Id { get; set; } = "";
            }
            """, references);
        Assert.Empty(app.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));

        using var stream = new MemoryStream();
        Assert.True(app.Emit(stream).Success);

        var tests = Generate("App.Tests", """
            using PowerSync.Common.DB;

            [GenerateSqlMapper]
            public partial class Fixture
            {
                public string Id { get; set; } = "";
            }
            """, references.Append(MetadataReference.CreateFromImage(stream.ToArray())));
        Assert.Empty(tests.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }
}
