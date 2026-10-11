// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
using Microsoft.CodeAnalysis.Text;
using TerraFX.CodeAnalysis;
using TerraFX.Diagnostics.Analyzers;

namespace TerraFX.Diagnostics.Fixers;

/// <summary>
/// Provides fixes that insert required statement-separation blank lines.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StatementSpacingCodeFixProvider)), Shared]
public sealed class StatementSpacingCodeFixProvider : SyntaxEditorBasedCodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [StatementSpacingAnalyzer.DiagnosticId];

    private static SyntaxNode? TryGetFixableNode(SyntaxNode root, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree != root.SyntaxTree)
        {
            return null;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var text = root.SyntaxTree.GetText();

        foreach (var candidate in node.AncestorsAndSelf())
        {
            var location = candidate is SwitchSectionSyntax section
                ? section.Labels[0].GetLocation()
                : candidate.GetFirstToken().GetLocation();

            if (location.SourceSpan != diagnostic.Location.SourceSpan)
            {
                continue;
            }

            var previous = StatementSpacing.GetPreviousSibling(candidate);

            if (previous is null)
            {
                if ((candidate.Parent is TypeDeclarationSyntax typeDeclaration)
                    && (typeDeclaration.Members.Count > 0)
                    && ReferenceEquals(typeDeclaration.Members[0], candidate)
                    && StatementSpacing.HasBlankLineAfterOpeningBrace(text, typeDeclaration))
                {
                    return candidate;
                }

                continue;
            }

            var requiresBlankLine = (diagnostic.Id == StatementSpacingAnalyzer.DiagnosticId)
                && StatementSpacing.RequiresBlankLineBetween(previous, candidate);

            if (!requiresBlankLine || StatementSpacing.HasBlankLineBetween(text, previous, candidate))
            {
                continue;
            }

            return candidate;
        }

        return null;
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
            if (TryGetFixableNode(root, diagnostic) is not null)
            {
                RegisterCodeFix(context, Resources.FixBlankLineSpacing, nameof(ApplyFixAsync));
                return;
            }
        }
    }

    /// <inheritdoc/>
    protected override async Task ApplyFixAsync(Document document, Diagnostic diagnostic, SyntaxEditor editor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var node = TryGetFixableNode(editor.OriginalRoot, diagnostic);

        if (node is null)
        {
            return;
        }

        var previous = StatementSpacing.GetPreviousSibling(node);
        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

        if (previous is null)
        {
            RemoveBlankLineAfterOpeningBrace(node, sourceText, editor);
            return;
        }

        var firstToken = node.GetFirstToken();
        var lineEnding = Environment.NewLine;
        var foundLineEnding = false;

        foreach (var trivia in firstToken.LeadingTrivia)
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                lineEnding = trivia.ToString();
                foundLineEnding = true;
                break;
            }
        }

        if (!foundLineEnding)
        {
            foreach (var trivia in editor.OriginalRoot.DescendantTrivia())
            {
                if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    lineEnding = trivia.ToString();
                    break;
                }
            }
        }

        var previousLine = previous.GetLastToken().GetLocation().GetLineSpan().StartLinePosition.Line;
        var currentLine = node.GetFirstToken().GetLocation().GetLineSpan().StartLinePosition.Line;
        var lineEndingCount = (previousLine == currentLine) ? 2 : 1;
        var indentation = (lineEndingCount > 1) ? GetLineIndentation(sourceText, previous.SpanStart) : null;

        if (lineEndingCount > 1)
        {
            editor.ReplaceNode(
                previous,
                (rewrittenPrevious, _) => {
                    var lastToken = rewrittenPrevious.GetLastToken();
                    var trailingTrivia = lastToken.TrailingTrivia;

                    while ((trailingTrivia.Count > 0) && trailingTrivia[trailingTrivia.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia))
                    {
                        trailingTrivia = trailingTrivia.RemoveAt(trailingTrivia.Count - 1);
                    }

                    return rewrittenPrevious.ReplaceToken(lastToken, lastToken.WithTrailingTrivia(trailingTrivia));
                });
        }

        editor.ReplaceNode(
            node,
            (rewrittenNode, _) => {
                var rewrittenToken = rewrittenNode.GetFirstToken();
                var leadingTrivia = rewrittenToken.LeadingTrivia;

                if (indentation is not null)
                {
                    while ((leadingTrivia.Count > 0) && leadingTrivia[0].IsKind(SyntaxKind.WhitespaceTrivia))
                    {
                        leadingTrivia = leadingTrivia.RemoveAt(0);
                    }

                    leadingTrivia = leadingTrivia.Insert(0, SyntaxFactory.Whitespace(indentation));
                }

                for (var index = 0; index < lineEndingCount; index++)
                {
                    leadingTrivia = leadingTrivia.Insert(0, SyntaxFactory.EndOfLine(lineEnding));
                }

                var updatedNode = rewrittenNode.ReplaceToken(rewrittenToken, rewrittenToken.WithLeadingTrivia(leadingTrivia));
                return updatedNode;
            });

    }

    private static void RemoveBlankLineAfterOpeningBrace(SyntaxNode node, SourceText sourceText, SyntaxEditor editor)
    {
        if ((node.Parent is not TypeDeclarationSyntax typeDeclaration)
            || (typeDeclaration.Members.Count == 0)
            || !ReferenceEquals(typeDeclaration.Members[0], node))
        {
            return;
        }

        var openingBraceLine = sourceText.Lines.GetLineFromPosition(typeDeclaration.OpenBraceToken.SpanStart).LineNumber;
        var blankLine = sourceText.Lines[openingBraceLine + 1];
        var blankLineSpan = blankLine.SpanIncludingLineBreak;

        editor.ReplaceNode(
            typeDeclaration,
            (rewrittenType, _) => {
                var updatedType = (TypeDeclarationSyntax)rewrittenType;
                var openingBrace = updatedType.OpenBraceToken;
                var firstToken = updatedType.Members[0].GetFirstToken();
                var updatedTrailingTrivia = RemoveTriviaWithinSpan(openingBrace.TrailingTrivia, blankLineSpan);
                var updatedLeadingTrivia = RemoveTriviaWithinSpan(firstToken.LeadingTrivia, blankLineSpan);

                updatedType = updatedType.ReplaceToken(openingBrace, openingBrace.WithTrailingTrivia(updatedTrailingTrivia));

                firstToken = updatedType.Members[0].GetFirstToken();
                return updatedType.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(updatedLeadingTrivia));
            });
    }

    private static SyntaxTriviaList RemoveTriviaWithinSpan(SyntaxTriviaList trivia, TextSpan span)
    {
        var remainingTrivia = SyntaxFactory.TriviaList();

        foreach (var item in trivia)
        {
            if ((item.FullSpan.End <= span.Start) || (item.FullSpan.Start >= span.End))
            {
                remainingTrivia = remainingTrivia.Add(item);
            }
        }

        return remainingTrivia;
    }

    private static string GetLineIndentation(SourceText text, int position)
    {
        var line = text.Lines.GetLineFromPosition(position);
        var lineText = line.ToString();
        var indentationLength = 0;

        while ((indentationLength < lineText.Length) && char.IsWhiteSpace(lineText[indentationLength]))
        {
            indentationLength++;
        }

        return lineText.Substring(0, indentationLength);
    }

    private static class Resources
    {
        private static readonly ResourceManager s_resourceManager = new ResourceManager("TerraFX.Diagnostics.Fixers.Resources", typeof(Resources).Assembly);

        internal static string FixBlankLineSpacing
        {
            get
            {
                var title = s_resourceManager.GetString(nameof(FixBlankLineSpacing), CultureInfo.CurrentUICulture);

                if (title is null)
                {
                    ThrowHelper.ThrowMissingManifestResourceException("The code-fix title resource is missing.");
                }

                return title;
            }
        }
    }
}
