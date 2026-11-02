namespace PowerSync.Common.SourceGenerators;

using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

internal static class Descriptors
{
    private const string Category = "PowerSync.SourceGenerators";

    public static readonly DiagnosticDescriptor MissingPowerSyncCommon = new(
        id: "PSSG001",
        title: "PowerSync.Common is not referenced",
        messageFormat: "[GenerateSqlMapper] requires a reference to PowerSync.Common",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotPartial = new(
        id: "PSSG002",
        title: "Type must be partial",
        messageFormat: "'{0}' must be declared partial to use [GenerateSqlMapper]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotConstructible = new(
        id: "PSSG003",
        title: "Type cannot be constructed",
        messageFormat: "[GenerateSqlMapper] cannot construct '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedMemberType = new(
        id: "PSSG004",
        title: "Unsupported member type",
        messageFormat: "[GenerateSqlMapper] cannot map '{0}' of type '{1}'. Mark the property with [Ignored] or implement IFromRow<T> manually.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}

internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location location) =>
        location.SourceTree is null ? null : new(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location location, params string[] arguments) =>
        new(descriptor, LocationInfo.From(location), new EquatableArray<string>(arguments.ToImmutableArray()));

    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.ToArray<object>());
}
