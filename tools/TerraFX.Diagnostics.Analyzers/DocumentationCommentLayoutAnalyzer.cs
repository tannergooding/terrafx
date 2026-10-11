// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using System.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TerraFX.CodeAnalysis;

namespace TerraFX.Diagnostics.Analyzers;

/// <summary>Requires single-paragraph XML documentation elements to fit on one line and continuation markers to align.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DocumentationCommentLayoutAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Identifies XML documentation comments with incorrect line layout.</summary>
    public const string DiagnosticId = "TFX0006";

    private static readonly LocalizableString s_title = new LocalizableResourceString(nameof(Resources.DocumentationCommentLayoutTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_message = new LocalizableResourceString(nameof(Resources.DocumentationCommentLayoutMessage), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_description = new LocalizableResourceString(nameof(Resources.DocumentationCommentLayoutDescription), Resources.ResourceManager, typeof(Resources));

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

    private static class Resources
    {
        internal const string DocumentationCommentLayoutDescription = nameof(DocumentationCommentLayoutDescription);
        internal const string DocumentationCommentLayoutMessage = nameof(DocumentationCommentLayoutMessage);
        internal const string DocumentationCommentLayoutTitle = nameof(DocumentationCommentLayoutTitle);

        internal static ResourceManager ResourceManager { get; } = new ResourceManager("TerraFX.Diagnostics.Analyzers.Resources", typeof(Resources).Assembly);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var sourceText = context.Tree.GetText(context.CancellationToken);

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.GetStructure() is not DocumentationCommentTriviaSyntax documentationComment
                || !DocumentationCommentLayout.HasFormattingIssues(documentationComment, sourceText))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(s_rule, trivia.GetLocation()));
        }
    }

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ThrowHelper.ThrowIfNull(context, nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }
}
