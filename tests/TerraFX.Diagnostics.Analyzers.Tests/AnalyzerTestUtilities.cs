// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TerraFX.Diagnostics.Analyzers;

namespace TerraFX.Tests;

internal static class AnalyzerTestUtilities
{
    private static readonly ImmutableArray<MetadataReference> s_metadataReferences = CreateMetadataReferences();

    public static ImmutableArray<MetadataReference> MetadataReferences => s_metadataReferences;

    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAutoImplementedPropertyLineAsync(Document document)
    {
        return await AnalyzeWithAnalyzerAsync(document, new AutoImplementedPropertyLineAnalyzer()).ConfigureAwait(false);
    }

    public static async Task<ImmutableArray<Diagnostic>> AnalyzeDocumentationCommentLayoutAsync(Document document)
    {
        return await AnalyzeWithAnalyzerAsync(document, new DocumentationCommentLayoutAnalyzer()).ConfigureAwait(false);
    }

    public static async Task<ImmutableArray<Diagnostic>> AnalyzeMemberOrderingAsync(Document document)
    {
        return await AnalyzeWithAnalyzerAsync(document, new MemberOrderingAnalyzer()).ConfigureAwait(false);
    }

    public static async Task<ImmutableArray<Diagnostic>> AnalyzeSwitchSectionBracesAsync(Document document)
    {
        return await AnalyzeWithAnalyzerAsync(document, new SwitchSectionBracesAnalyzer()).ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeWithAnalyzerAsync(Document document, DiagnosticAnalyzer analyzer)
    {
        var compilation = await document.Project.GetCompilationAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("The document project does not have a compilation.");
        var syntaxTree = await document.GetSyntaxTreeAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("The test document does not have a syntax tree.");

        var diagnostics = await compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync()
            .ConfigureAwait(false);

        return [.. diagnostics.Where(diagnostic => diagnostic.Location.SourceTree == syntaxTree)];
    }

    private static ImmutableArray<MetadataReference> CreateMetadataReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("The runtime did not provide trusted platform assemblies.");

        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    }
}
