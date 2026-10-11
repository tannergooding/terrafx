// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using TerraFX.CodeAnalysis;
using TerraFX.Diagnostics.Analyzers;

namespace TerraFX.Diagnostics.Fixers;

/// <summary>Provides a fix for XML documentation comment layout.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DocumentationCommentLayoutCodeFixProvider)), System.Composition.Shared]
public sealed class DocumentationCommentLayoutCodeFixProvider : CodeFixProvider
{
    private static readonly ResourceManager s_resourceManager = new ResourceManager("TerraFX.Diagnostics.Fixers.Resources", typeof(DocumentationCommentLayoutCodeFixProvider).Assembly);

    private static string FormatDocumentationComment
    {
        get
        {
            var title = s_resourceManager.GetString(nameof(FormatDocumentationComment), CultureInfo.CurrentUICulture);

            if (title is null)
            {
                ThrowHelper.ThrowMissingManifestResourceException("The code-fix title resource is missing.");
            }

            return title;
        }
    }

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [DocumentationCommentLayoutAnalyzer.DiagnosticId];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    private static DocumentationCommentTriviaSyntax? TryGetDocumentationComment(SyntaxNode root, Diagnostic diagnostic)
    {
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: false))
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                && trivia.Span.Contains(diagnostic.Location.SourceSpan.Start)
                && trivia.GetStructure() is DocumentationCommentTriviaSyntax documentationComment)
            {
                return documentationComment;
            }
        }

        return null;
    }

    private static bool IntersectsAny(List<TextSpan> spans, TextSpan candidateSpan)
    {
        foreach (var span in spans)
        {
            if (span.IntersectsWith(candidateSpan))
            {
                return true;
            }
        }

        return false;
    }

    private static string CollapseXmlWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var insideTag = false;
        var quote = '\0';

        foreach (var character in text)
        {
            if (quote != '\0')
            {
                _ = builder.Append(character);

                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if ((builder.Length != 0) && (builder[builder.Length - 1] != ' '))
                {
                    _ = builder.Append(' ');
                }

                continue;
            }

            if (insideTag && (character is '"' or '\''))
            {
                quote = character;
            }
            else if (character == '<')
            {
                insideTag = true;
            }
            else if (character == '>')
            {
                insideTag = false;
            }

            _ = builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    private static string RemoveDocumentationExteriors(string text)
    {
        var builder = new StringBuilder(text.Length);
        var lineStart = 0;

        while (lineStart < text.Length)
        {
            var lineEnd = lineStart;

            while ((lineEnd < text.Length) && (text[lineEnd] is not ('\r' or '\n')))
            {
                lineEnd++;
            }

            var line = text.Substring(lineStart, lineEnd - lineStart);
            var markerIndex = DocumentationCommentLayout.FindExteriorMarker(line);

            if ((markerIndex >= 0) && DocumentationCommentLayout.IsWhitespace(line, markerIndex))
            {
                var contentStart = markerIndex + 3;

                if ((contentStart < line.Length) && char.IsWhiteSpace(line[contentStart]))
                {
                    contentStart++;
                }

                line = line.Substring(contentStart);
            }

            _ = builder.Append(line);

            if (lineEnd < text.Length)
            {
                if ((text[lineEnd] == '\r') && (lineEnd + 1 < text.Length) && (text[lineEnd + 1] == '\n'))
                {
                    lineEnd++;
                }

                _ = builder.Append(' ');
                lineStart = lineEnd + 1;
            }
            else
            {
                lineStart = text.Length;
            }
        }

        return builder.ToString();
    }

    private static string CollapseElement(XmlElementSyntax element, SourceText sourceText)
    {
        var startTag = sourceText.ToString(element.StartTag.Span);
        var contentSpan = TextSpan.FromBounds(element.StartTag.Span.End, element.EndTag.SpanStart);
        var content = sourceText.ToString(contentSpan);
        var endTag = sourceText.ToString(element.EndTag.Span);

        return CollapseXmlWhitespace(RemoveDocumentationExteriors(startTag))
            + CollapseXmlWhitespace(RemoveDocumentationExteriors(content))
            + CollapseXmlWhitespace(RemoveDocumentationExteriors(endTag));
    }

    private static async Task<Document> ApplyFixAsync(Document document, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

        if (root is null)
        {
            ThrowHelper.ThrowInvalidOperationException("The document does not have a syntax root.");
        }

        var documentationComment = TryGetDocumentationComment(root, diagnostic);

        if (documentationComment is null)
        {
            return document;
        }

        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var indentation = DocumentationCommentLayout.GetIndentation(documentationComment, sourceText);

        if (indentation is null)
        {
            return document;
        }

        var changes = new List<TextChange>();
        var collapsedSpans = new List<TextSpan>();

        var elementsToCollapse = DocumentationCommentLayout.GetElementsToCollapse(documentationComment, sourceText);

        foreach (var element in elementsToCollapse)
        {
            var firstLine = sourceText.Lines.GetLineFromPosition(element.SpanStart);
            var lastLine = sourceText.Lines.GetLineFromPosition(element.Span.End - 1);
            var replacementSpan = TextSpan.FromBounds(firstLine.Start, lastLine.End);
            var prefix = sourceText.ToString(TextSpan.FromBounds(firstLine.Start, element.SpanStart));
            var markerIndex = DocumentationCommentLayout.FindExteriorMarker(prefix);

            if ((markerIndex >= 0) && DocumentationCommentLayout.IsWhitespace(prefix, markerIndex))
            {
                prefix = indentation + prefix.Substring(markerIndex);
            }

            var suffix = sourceText.ToString(TextSpan.FromBounds(element.Span.End, lastLine.End));
            var normalizedElement = CollapseElement(element, sourceText);
            var replacement = prefix + normalizedElement + suffix;

            if (!string.Equals(sourceText.ToString(replacementSpan), replacement, System.StringComparison.Ordinal))
            {
                changes.Add(new TextChange(replacementSpan, replacement));
                collapsedSpans.Add(replacementSpan);
            }
        }

        foreach (var line in DocumentationCommentLayout.GetMisalignedLines(documentationComment, sourceText))
        {
            if (IntersectsAny(collapsedSpans, line.Span))
            {
                continue;
            }

            var lineText = line.ToString();
            var markerIndex = DocumentationCommentLayout.FindExteriorMarker(lineText);

            if (markerIndex >= 0)
            {
                changes.Add(new TextChange(new TextSpan(line.Start, markerIndex), indentation));
            }
        }

        if (changes.Count == 0)
        {
            return document;
        }

        changes.Sort(static (left, right) => left.Span.Start.CompareTo(right.Span.Start));
        var updatedText = sourceText.WithChanges(changes);

        return document.WithText(updatedText);
    }

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            var codeAction = CodeAction.Create(
                FormatDocumentationComment,
                cancellationToken => ApplyFixAsync(context.Document, diagnostic, cancellationToken),
                nameof(ApplyFixAsync));

            context.RegisterCodeFix(codeAction, diagnostic);
        }

        return Task.CompletedTask;
    }
}
