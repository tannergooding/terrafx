// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TerraFX.CodeAnalysis;

internal static class StatementSpacing
{
    internal static IEnumerable<SyntaxNode> GetSiblings(SyntaxNode container)
    {
        switch (container)
        {
            case BlockSyntax block:
            {
                foreach (var statement in block.Statements)
                {
                    yield return statement;
                }

                break;
            }

            case CompilationUnitSyntax compilationUnit:
            {
                foreach (var member in compilationUnit.Members)
                {
                    yield return member;
                }

                break;
            }

            case SwitchStatementSyntax switchStatement:
            {
                foreach (var section in switchStatement.Sections)
                {
                    yield return section;
                }

                break;
            }

            case TypeDeclarationSyntax typeDeclaration:
            {
                foreach (var member in typeDeclaration.Members)
                {
                    yield return member;
                }

                break;
            }

            default:
            {
                break;
            }
        }
    }

    internal static SyntaxNode? GetPreviousSibling(SyntaxNode node)
    {
        if (node.Parent is null)
        {
            return null;
        }

        SyntaxNode? previous = null;

        foreach (var sibling in GetSiblings(node.Parent))
        {
            if (ReferenceEquals(sibling, node))
            {
                return previous;
            }

            previous = sibling;
        }

        return null;
    }

    internal static bool RequiresBlankLineAfterTerminalBrace(SyntaxNode previous, SyntaxNode current)
    {
        if ((previous is SwitchSectionSyntax) && (current is SwitchSectionSyntax))
        {
            return true;
        }

        if (!previous.GetLastToken().IsKind(SyntaxKind.CloseBraceToken))
        {
            return false;
        }

        var previousIsStatement = GetStatement(previous) is not null;
        var currentIsStatement = GetStatement(current) is not null;
        var previousIsMember = previous is MemberDeclarationSyntax;
        var currentIsMember = current is MemberDeclarationSyntax;

        return (previousIsStatement && currentIsStatement)
            || (previousIsMember && currentIsMember);
    }

    internal static bool HasBlankLineAfterOpeningBrace(SourceText text, TypeDeclarationSyntax typeDeclaration)
    {
        if (typeDeclaration.Members.Count == 0)
        {
            return false;
        }

        var openingBraceLine = text.Lines.GetLineFromPosition(typeDeclaration.OpenBraceToken.SpanStart).LineNumber;
        var firstMemberLine = text.Lines.GetLineFromPosition(typeDeclaration.Members[0].SpanStart).LineNumber;
        var blankLineNumber = openingBraceLine + 1;

        return (firstMemberLine > blankLineNumber)
            && string.IsNullOrWhiteSpace(text.ToString(text.Lines[blankLineNumber].Span));
    }

    internal static bool RequiresBlankLineBeforeControlFlow(SyntaxNode previous, SyntaxNode current)
    {
        var previousStatement = GetStatement(previous);
        var currentStatement = GetStatement(current);

        if ((previousStatement is null) || (currentStatement is null))
        {
            return false;
        }

        return currentStatement is IfStatementSyntax
            or SwitchStatementSyntax
            or ForStatementSyntax
            or ForEachStatementSyntax
            or ForEachVariableStatementSyntax
            or WhileStatementSyntax
            or DoStatementSyntax
            or TryStatementSyntax;
    }

    internal static bool RequiresBlankLineBetween(SyntaxNode previous, SyntaxNode current)
    {
        return RequiresBlankLineAfterTerminalBrace(previous, current)
            || RequiresBlankLineBeforeControlFlow(previous, current);
    }

    internal static bool HasBlankLineBetween(SourceText text, SyntaxNode previous, SyntaxNode current)
    {
        var previousLine = text.Lines.GetLineFromPosition(previous.Span.End).LineNumber;
        var currentLine = text.Lines.GetLineFromPosition(current.SpanStart).LineNumber;

        for (var lineNumber = previousLine + 1; lineNumber < currentLine; lineNumber++)
        {
            if (string.IsNullOrWhiteSpace(text.ToString(text.Lines[lineNumber].Span)))
            {
                return true;
            }
        }

        return false;
    }

    private static StatementSyntax? GetStatement(SyntaxNode node) =>
        node switch {
            StatementSyntax statement => statement,
            GlobalStatementSyntax globalStatement => globalStatement.Statement,
            _ => null,
        };
}
