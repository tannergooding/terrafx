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
/// Requires each C# source file to contain exactly one top-level type declaration.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingleTopLevelTypePerFileAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies files that do not contain exactly one top-level type declaration.
    /// </summary>
    public const string DiagnosticId = "TFX0004";

    private static readonly LocalizableString s_title = new LocalizableResourceString(nameof(Resources.SingleTopLevelTypePerFileTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_message = new LocalizableResourceString(nameof(Resources.SingleTopLevelTypePerFileMessage), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_description = new LocalizableResourceString(nameof(Resources.SingleTopLevelTypePerFileDescription), Resources.ResourceManager, typeof(Resources));

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

    private static void AnalyzeMembers(SyntaxList<MemberDeclarationSyntax> members, SyntaxNodeAnalysisContext context, ref bool hasType)
    {
        foreach (var member in members)
        {
            if (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                AnalyzeMembers(namespaceDeclaration.Members, context, ref hasType);
                continue;
            }

            if (member is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                continue;
            }

            if (!hasType)
            {
                hasType = true;
                continue;
            }

            var identifier = member switch {
                BaseTypeDeclarationSyntax type => type.Identifier,
                DelegateDeclarationSyntax type => type.Identifier,
                _ => default,
            };

            context.ReportDiagnostic(Diagnostic.Create(s_rule, identifier.GetLocation()));
        }
    }

    private static void AnalyzeCompilationUnit(SyntaxNodeAnalysisContext context)
    {
        var compilationUnit = (CompilationUnitSyntax)context.Node;
        var hasType = false;
        AnalyzeMembers(compilationUnit.Members, context, ref hasType);

        if (!hasType)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, compilationUnit.GetLocation()));
        }
    }

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ThrowHelper.ThrowIfNull(context, nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCompilationUnit, SyntaxKind.CompilationUnit);
    }

    private static class Resources
    {
        internal const string SingleTopLevelTypePerFileDescription = nameof(SingleTopLevelTypePerFileDescription);
        internal const string SingleTopLevelTypePerFileMessage = nameof(SingleTopLevelTypePerFileMessage);
        internal const string SingleTopLevelTypePerFileTitle = nameof(SingleTopLevelTypePerFileTitle);

        internal static ResourceManager ResourceManager { get; } = new ResourceManager("TerraFX.Diagnostics.Analyzers.Resources", typeof(Resources).Assembly);
    }
}
