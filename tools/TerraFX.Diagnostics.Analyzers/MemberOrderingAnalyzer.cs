// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using System.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TerraFX.CodeAnalysis;

namespace TerraFX.Diagnostics.Analyzers;

/// <summary>
/// Requires type members to follow the repository's discoverability ordering.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MemberOrderingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier for type-member ordering.
    /// </summary>
    public const string DiagnosticId = "TFX0002";

    private static readonly LocalizableString s_title = new LocalizableResourceString(nameof(Resources.MemberOrderingTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_message = new LocalizableResourceString(nameof(Resources.MemberOrderingMessage), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_description = new LocalizableResourceString(nameof(Resources.MemberOrderingDescription), Resources.ResourceManager, typeof(Resources));

    private static readonly DiagnosticDescriptor s_rule = new DiagnosticDescriptor(
        DiagnosticId,
        s_title,
        s_message,
        "Style",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: s_description);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [s_rule];

    private static void AnalyzeType(SyntaxNodeAnalysisContext context)
    {
        var type = (TypeDeclarationSyntax)context.Node;

        if (!MemberOrdering.IsOrdered(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, type.Identifier.GetLocation()));
        }
    }

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ThrowHelper.ThrowIfNull(context, nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeType, SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.RecordDeclaration);
    }

    private static class Resources
    {
        internal const string MemberOrderingDescription = nameof(MemberOrderingDescription);
        internal const string MemberOrderingMessage = nameof(MemberOrderingMessage);
        internal const string MemberOrderingTitle = nameof(MemberOrderingTitle);

        internal static ResourceManager ResourceManager { get; } = new ResourceManager("TerraFX.Diagnostics.Analyzers.Resources", typeof(Resources).Assembly);
    }
}
