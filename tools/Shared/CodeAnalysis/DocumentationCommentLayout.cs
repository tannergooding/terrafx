// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TerraFX.CodeAnalysis;

internal static class DocumentationCommentLayout
{
    internal static List<XmlElementSyntax> GetElementsToCollapse(DocumentationCommentTriviaSyntax documentationComment, SourceText sourceText)
    {
        var elements = new List<XmlElementSyntax>();

        foreach (var node in documentationComment.DescendantNodes())
        {
            if (node is not XmlElementSyntax element || !IsSingleParagraphElement(element, sourceText))
            {
                continue;
            }

            var hasCollapsibleAncestor = false;

            for (var ancestor = element.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ancestor is XmlElementSyntax ancestorElement && IsSingleParagraphElement(ancestorElement, sourceText))
                {
                    hasCollapsibleAncestor = true;
                    break;
                }
            }

            if (!hasCollapsibleAncestor)
            {
                elements.Add(element);
            }
        }

        return elements;
    }

    internal static string? GetIndentation(DocumentationCommentTriviaSyntax documentationComment, SourceText sourceText)
    {
        if (documentationComment.Span.IsEmpty)
        {
            return null;
        }

        var firstLine = sourceText.Lines.GetLineFromPosition(documentationComment.SpanStart);
        var firstLineText = firstLine.ToString();
        var markerIndex = FindExteriorMarker(firstLineText);

        if (markerIndex < 0 || !IsWhitespace(firstLineText, markerIndex))
        {
            return null;
        }

        return firstLineText.Substring(0, markerIndex);
    }

    internal static List<TextLine> GetMisalignedLines(DocumentationCommentTriviaSyntax documentationComment, SourceText sourceText)
    {
        var misalignedLines = new List<TextLine>();

        if (documentationComment.Span.IsEmpty)
        {
            return misalignedLines;
        }

        var indentation = GetIndentation(documentationComment, sourceText);

        if (indentation is null)
        {
            return misalignedLines;
        }

        var firstLine = sourceText.Lines.GetLineFromPosition(documentationComment.SpanStart).LineNumber;
        var lastLine = sourceText.Lines.GetLineFromPosition(documentationComment.Span.End - 1).LineNumber;

        for (var lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
        {
            var line = sourceText.Lines[lineNumber];
            var lineText = line.ToString();
            var markerIndex = FindExteriorMarker(lineText);

            if ((markerIndex >= 0)
                && IsWhitespace(lineText, markerIndex)
                && !string.Equals(lineText.Substring(0, markerIndex), indentation, StringComparison.Ordinal))
            {
                misalignedLines.Add(line);
            }
        }

        return misalignedLines;
    }

    internal static int FindExteriorMarker(string text)
        => text.IndexOf("///", StringComparison.Ordinal);

    internal static bool IsWhitespace(string text, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool HasFormattingIssues(DocumentationCommentTriviaSyntax documentationComment, SourceText sourceText)
        => (GetElementsToCollapse(documentationComment, sourceText).Count != 0)
            || (GetMisalignedLines(documentationComment, sourceText).Count != 0);

    private static bool IsSingleParagraphElement(XmlElementSyntax element, SourceText sourceText)
    {
        if (element.StartTag.IsMissing || element.EndTag.IsMissing || IsMultilineBlockTag(element.StartTag.Name.LocalName.ValueText))
        {
            return false;
        }

        foreach (var content in element.Content)
        {
            if (content is not (XmlTextSyntax or XmlElementSyntax or XmlEmptyElementSyntax))
            {
                return false;
            }
        }

        foreach (var descendant in element.DescendantNodes())
        {
            if (descendant is XmlElementSyntax nestedElement
                && IsBlockTag(nestedElement.StartTag.Name.LocalName.ValueText))
            {
                return false;
            }

            if (descendant is XmlEmptyElementSyntax emptyElement
                && IsBlockTag(emptyElement.Name.LocalName.ValueText))
            {
                return false;
            }
        }

        var firstLine = sourceText.Lines.GetLineFromPosition(element.SpanStart).LineNumber;
        var lastLine = sourceText.Lines.GetLineFromPosition(element.Span.End - 1).LineNumber;

        if (firstLine == lastLine)
        {
            return false;
        }

        var hasContentBeforeBreak = false;
        var hasBreakAfterContent = false;

        for (var lineNumber = firstLine + 1; lineNumber < lastLine; lineNumber++)
        {
            var lineText = sourceText.Lines[lineNumber].ToString();
            var markerIndex = FindExteriorMarker(lineText);
            var contentStart = markerIndex >= 0 ? markerIndex + 3 : 0;
            var content = lineText.Substring(contentStart).Trim();

            if (content.Length == 0)
            {
                hasBreakAfterContent |= hasContentBeforeBreak;
                continue;
            }

            if (hasBreakAfterContent)
            {
                return false;
            }

            hasContentBeforeBreak = true;
        }

        return true;
    }

    private static bool IsBlockTag(string name)
        => name is "code" or "example" or "item" or "list" or "para";

    private static bool IsMultilineBlockTag(string name)
        => name is "code" or "example" or "item" or "list";
}
