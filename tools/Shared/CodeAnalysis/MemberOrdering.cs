// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TerraFX.CodeAnalysis;

internal static class MemberOrdering
{
    internal static SyntaxList<MemberDeclarationSyntax> GetOrderedMembers(TypeDeclarationSyntax type)
    {
        var orderedEntries = GetOrderedEntries(type);
        return SyntaxFactory.List(orderedEntries.Select(static entry => entry.Member));
    }

    internal static bool IsOrdered(TypeDeclarationSyntax type)
    {
        var orderedEntries = GetOrderedEntries(type);

        for (var index = 0; index < orderedEntries.Count; index++)
        {
            if (orderedEntries[index].Index != index)
            {
                return false;
            }
        }

        return true;
    }

    private static void AppendOrderedMembers(List<OrderingEntry> entries, TypeDeclarationSyntax type, List<OrderingEntry> destination)
    {
        if (entries.Count == 0)
        {
            return;
        }

        var orderedMembers = entries
            .OrderBy(static entry => GetCategory(entry.Member))
            .ThenBy(entry => IsLayoutSensitiveField(entry.Member, type) ? 0 : 1)
            .ThenBy(entry => IsLayoutSensitiveField(entry.Member, type) ? entry.Index : 0)
            .ThenBy(static entry => IsStatic(entry.Member) ? 0 : 1)
            .ThenBy(entry => GetAccessibility(entry.Member, type))
            .ThenBy(static entry => GetName(entry.Member), System.StringComparer.Ordinal)
            .ThenBy(static entry => GetSignature(entry.Member), System.StringComparer.Ordinal)
            .ThenBy(static entry => entry.Index);

        destination.AddRange(orderedMembers);
        entries.Clear();
    }

    private static int GetAccessibility(MemberDeclarationSyntax member, TypeDeclarationSyntax containingType)
    {
        var modifiers = GetModifiers(member);

        if (modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return 0;
        }

        if (modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return 1;
        }

        if (modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return 2;
        }

        if (modifiers.Any(SyntaxKind.PrivateKeyword))
        {
            return 3;
        }

        return containingType is InterfaceDeclarationSyntax ? 0 : 3;
    }

    private static int GetCategory(MemberDeclarationSyntax member)
    {
        return member switch {
            FieldDeclarationSyntax field when field.Modifiers.Any(SyntaxKind.ConstKeyword) => 0,
            FieldDeclarationSyntax or EventFieldDeclarationSyntax => 1,
            ConstructorDeclarationSyntax => 2,
            DestructorDeclarationSyntax => 3,
            PropertyDeclarationSyntax or EventDeclarationSyntax => 4,
            IndexerDeclarationSyntax => 5,
            ConversionOperatorDeclarationSyntax => 6,
            OperatorDeclarationSyntax => 7,
            MethodDeclarationSyntax => 8,
            _ => 9,
        };
    }

    private static SyntaxTokenList GetModifiers(MemberDeclarationSyntax member)
    {
        return member switch {
            BaseFieldDeclarationSyntax field => field.Modifiers,
            BaseMethodDeclarationSyntax method => method.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            BaseTypeDeclarationSyntax type => type.Modifiers,
            DelegateDeclarationSyntax declaration => declaration.Modifiers,
            _ => default,
        };
    }

    private static string GetName(MemberDeclarationSyntax member)
    {
        return member switch {
            FieldDeclarationSyntax field => field.Declaration.Variables[0].Identifier.ValueText,
            EventFieldDeclarationSyntax eventField => eventField.Declaration.Variables[0].Identifier.ValueText,
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            BaseMethodDeclarationSyntax method => method switch {
                ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
                DestructorDeclarationSyntax destructor => destructor.Identifier.ValueText,
                ConversionOperatorDeclarationSyntax conversion => conversion.ImplicitOrExplicitKeyword.ValueText,
                OperatorDeclarationSyntax operation => operation.OperatorToken.ValueText,
                MethodDeclarationSyntax declaration => declaration.Identifier.ValueText,
                _ => method.Kind().ToString(),
            },
            BasePropertyDeclarationSyntax property => property switch {
                PropertyDeclarationSyntax declaration => declaration.Identifier.ValueText,
                EventDeclarationSyntax declaration => declaration.Identifier.ValueText,
                IndexerDeclarationSyntax => "this",
                _ => property.Kind().ToString(),
            },
            DelegateDeclarationSyntax declaration => declaration.Identifier.ValueText,
            _ => member.Kind().ToString(),
        };
    }

    private static List<OrderingEntry> GetOrderedEntries(TypeDeclarationSyntax type)
    {
        var orderedEntries = new List<OrderingEntry>(type.Members.Count);
        var sortableEntries = new List<OrderingEntry>();

        for (var index = 0; index < type.Members.Count; index++)
        {
            var member = type.Members[index];
            var entry = new OrderingEntry(member, index);

            if (IsOrderingBarrier(member))
            {
                AppendOrderedMembers(sortableEntries, type, orderedEntries);
                orderedEntries.Add(entry);
            }
            else
            {
                sortableEntries.Add(entry);
            }
        }

        AppendOrderedMembers(sortableEntries, type, orderedEntries);
        return orderedEntries;
    }

    private static string GetSignature(MemberDeclarationSyntax member)
    {
        return member switch {
            BaseMethodDeclarationSyntax method => method.ParameterList.ToString(),
            IndexerDeclarationSyntax indexer => indexer.ParameterList.ToString(),
            _ => string.Empty,
        };
    }

    private static bool IsLayoutSensitiveField(MemberDeclarationSyntax member, TypeDeclarationSyntax containingType)
    {
        if (member is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return false;
        }

        if (member is not (FieldDeclarationSyntax or EventFieldDeclarationSyntax))
        {
            return false;
        }

        return (containingType is StructDeclarationSyntax)
            || (containingType is RecordDeclarationSyntax record && record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword));
    }

    private static bool IsOrderingBarrier(MemberDeclarationSyntax member)
    {
        if (member.ContainsDirectives)
        {
            return true;
        }

        return member switch {
            FieldDeclarationSyntax field => !field.Modifiers.Any(SyntaxKind.ConstKeyword) && field.Declaration.Variables.Any(static variable => variable.Initializer is not null),
            EventFieldDeclarationSyntax eventField => eventField.Declaration.Variables.Any(static variable => variable.Initializer is not null),
            PropertyDeclarationSyntax property => property.Initializer is not null,
            _ => false,
        };
    }

    private static bool IsStatic(MemberDeclarationSyntax member)
    {
        if (member is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return true;
        }

        return GetModifiers(member).Any(SyntaxKind.StaticKeyword);
    }

    private readonly struct OrderingEntry
    {
        private readonly int _index;
        private readonly MemberDeclarationSyntax _member;

        internal OrderingEntry(MemberDeclarationSyntax member, int index)
        {
            _member = member;
            _index = index;
        }

        internal int Index => _index;

        internal MemberDeclarationSyntax Member => _member;
    }
}
