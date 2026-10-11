// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using TerraFX.CodeAnalysis;
using TerraFX.Diagnostics.Analyzers;

namespace TerraFX.Diagnostics.Fixers;

/// <summary>
/// Provides a fix that wraps switch section statements in braces.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SwitchSectionBracesCodeFixProvider)), Shared]
public sealed class SwitchSectionBracesCodeFixProvider : SyntaxEditorBasedCodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [SwitchSectionBracesAnalyzer.DiagnosticId];

    private static SwitchSectionSyntax? TryGetFixableSection(SyntaxNode root, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree != root.SyntaxTree)
        {
            return null;
        }

        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var node = root.FindNode(diagnosticSpan);
        var section = node.FirstAncestorOrSelf<SwitchSectionSyntax>();

        if (section is null)
        {
            return null;
        }

        if ((section.Statements.Count == 0) || ((section.Statements.Count == 1) && (section.Statements[0] is BlockSyntax)))
        {
            return null;
        }

        var firstStatement = section.Statements[0];
        var lastStatement = section.Statements[section.Statements.Count - 1];
        var sectionStatementsSpan = TextSpan.FromBounds(firstStatement.SpanStart, lastStatement.Span.End);

        if (diagnosticSpan != sectionStatementsSpan)
        {
            return null;
        }

        return section;
    }

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The document does not have a syntax root.");
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (TryGetFixableSection(root, diagnostic) is not null)
            {
                RegisterCodeFix(context, Resources.AddBracesToSwitchSection, nameof(ApplyFixAsync));
                return;
            }
        }
    }

    /// <inheritdoc/>
    protected override Task ApplyFixAsync(Document document, Diagnostic diagnostic, SyntaxEditor editor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var section = TryGetFixableSection(editor.OriginalRoot, diagnostic);

        if (section is null)
        {
            return Task.CompletedTask;
        }

        editor.ReplaceNode(
            section,
            (rewrittenNode, _) => {
                var rewrittenSection = (SwitchSectionSyntax)rewrittenNode;
                var block = SyntaxFactory.Block(rewrittenSection.Statements);
                return rewrittenSection
                    .WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(block))
                    .WithAdditionalAnnotations(Formatter.Annotation);
            });

        return Task.CompletedTask;
    }

    private static class Resources
    {
        private static readonly ResourceManager s_resourceManager = new ResourceManager("TerraFX.Diagnostics.Fixers.Resources", typeof(Resources).Assembly);

        internal static string AddBracesToSwitchSection
        {
            get
            {
                var title = s_resourceManager.GetString(nameof(AddBracesToSwitchSection), CultureInfo.CurrentUICulture);

                if (title is null)
                {
                    ThrowHelper.ThrowMissingManifestResourceException("The code-fix title resource is missing.");
                }

                return title;
            }
        }
    }
}
