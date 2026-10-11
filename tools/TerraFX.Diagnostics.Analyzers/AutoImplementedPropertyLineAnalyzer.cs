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
/// Requires auto-implemented properties to fit on a single line.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AutoImplementedPropertyLineAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Identifies auto-implemented properties that span multiple lines.
    /// </summary>
    public const string DiagnosticId = "TFX0005";

    private static readonly LocalizableString s_title = new LocalizableResourceString(nameof(Resources.AutoImplementedPropertyLineTitle), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_message = new LocalizableResourceString(nameof(Resources.AutoImplementedPropertyLineMessage), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_description = new LocalizableResourceString(nameof(Resources.AutoImplementedPropertyLineDescription), Resources.ResourceManager, typeof(Resources));

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

    private static bool IsAutoImplementedProperty(PropertyDeclarationSyntax property)
    {
        var accessors = property.AccessorList?.Accessors;

        if ((accessors is null) || (accessors.Value.Count == 0) || (property.ExpressionBody is not null))
        {
            return false;
        }

        foreach (var accessor in accessors.Value)
        {
            if ((accessor.Body is not null)
                || (accessor.ExpressionBody is not null)
                || accessor.SemicolonToken.IsMissing)
            {
                return false;
            }
        }

        return true;
    }

    private static void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;

        if (!IsAutoImplementedProperty(property))
        {
            return;
        }

        var text = property.SyntaxTree.GetText(context.CancellationToken);
        var firstLine = text.Lines.GetLineFromPosition(property.SpanStart).LineNumber;
        var lastLine = text.Lines.GetLineFromPosition(property.Span.End - 1).LineNumber;

        if (firstLine != lastLine)
        {
            context.ReportDiagnostic(Diagnostic.Create(s_rule, property.GetLocation()));
        }
    }

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ThrowHelper.ThrowIfNull(context, nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private static class Resources
    {
        internal const string AutoImplementedPropertyLineDescription = nameof(AutoImplementedPropertyLineDescription);
        internal const string AutoImplementedPropertyLineMessage = nameof(AutoImplementedPropertyLineMessage);
        internal const string AutoImplementedPropertyLineTitle = nameof(AutoImplementedPropertyLineTitle);

        internal static ResourceManager ResourceManager { get; } = new ResourceManager("TerraFX.Diagnostics.Analyzers.Resources", typeof(Resources).Assembly);
    }
}
