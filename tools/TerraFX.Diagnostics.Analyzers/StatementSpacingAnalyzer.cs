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
/// Enforces blank-line spacing around type members and selected statement siblings.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatementSpacingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies statement-spacing diagnostics.
    /// </summary>
    public const string DiagnosticId = "TFX0003";

    private static readonly LocalizableString s_title = new LocalizableResourceString(nameof(Resources.StatementSpacingTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_message = new LocalizableResourceString(nameof(Resources.StatementSpacingMessage), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_description = new LocalizableResourceString(nameof(Resources.StatementSpacingDescription), Resources.ResourceManager, typeof(Resources));

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

    private static void AnalyzeSiblingSpacing(SyntaxNodeAnalysisContext context)
    {
        var text = context.Node.SyntaxTree.GetText(context.CancellationToken);

        if ((context.Node is TypeDeclarationSyntax typeDeclaration)
            && StatementSpacing.HasBlankLineAfterOpeningBrace(text, typeDeclaration))
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, typeDeclaration.Members[0].GetFirstToken().GetLocation()));
        }

        SyntaxNode? previous = null;

        foreach (var current in StatementSpacing.GetSiblings(context.Node))
        {
            if ((previous is not null)
                && StatementSpacing.RequiresBlankLineBetween(previous, current)
                && !StatementSpacing.HasBlankLineBetween(text, previous, current))
            {
                var location = current is SwitchSectionSyntax section
                    ? section.Labels[0].GetLocation()
                    : current.GetFirstToken().GetLocation();

                context.ReportDiagnostic(Diagnostic.Create(s_rule, location));
            }

            previous = current;
        }
    }

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ThrowHelper.ThrowIfNull(context, nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeSiblingSpacing,
            SyntaxKind.Block,
            SyntaxKind.CompilationUnit,
            SyntaxKind.SwitchStatement,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.InterfaceDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration);
    }

    private static class Resources
    {
        internal const string StatementSpacingDescription = nameof(StatementSpacingDescription);
        internal const string StatementSpacingMessage = nameof(StatementSpacingMessage);
        internal const string StatementSpacingTitle = nameof(StatementSpacingTitle);

        internal static ResourceManager ResourceManager { get; } = new ResourceManager("TerraFX.Diagnostics.Analyzers.Resources", typeof(Resources).Assembly);
    }
}
