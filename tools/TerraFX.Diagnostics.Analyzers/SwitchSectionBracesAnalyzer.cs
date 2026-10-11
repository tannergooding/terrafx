// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using System.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TerraFX.CodeAnalysis;

namespace TerraFX.Diagnostics.Analyzers;

/// <summary>
/// Requires every nonempty switch section to contain one braced statement block.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SwitchSectionBracesAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The diagnostic identifier for switch-section brace enforcement.
    /// </summary>
    public const string DiagnosticId = "TFX0001";

    private static readonly LocalizableString s_title = new LocalizableResourceString(nameof(Resources.Title), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_message = new LocalizableResourceString(nameof(Resources.Message), Resources.ResourceManager, typeof(Resources));
    private static readonly LocalizableString s_description = new LocalizableResourceString(nameof(Resources.Description), Resources.ResourceManager, typeof(Resources));

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

    private static void AnalyzeSwitchSection(SyntaxNodeAnalysisContext context)
    {
        var switchSection = (SwitchSectionSyntax)context.Node;

        if (switchSection.Statements.Count == 0)
        {
            return;
        }

        if ((switchSection.Statements.Count == 1) && (switchSection.Statements[0] is BlockSyntax))
        {
            return;
        }

        var firstStatement = switchSection.Statements[0];
        var lastStatement = switchSection.Statements[switchSection.Statements.Count - 1];
        var diagnosticSpan = TextSpan.FromBounds(firstStatement.SpanStart, lastStatement.Span.End);
        context.ReportDiagnostic(Diagnostic.Create(s_rule, Location.Create(switchSection.SyntaxTree, diagnosticSpan)));
    }

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        ThrowHelper.ThrowIfNull(context, nameof(context));

        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeSwitchSection, SyntaxKind.SwitchSection);
    }

    private static class Resources
    {
        internal static ResourceManager ResourceManager { get; } = new ResourceManager("TerraFX.Diagnostics.Analyzers.Resources", typeof(Resources).Assembly);
        internal const string Description = nameof(Description);
        internal const string Message = nameof(Message);

        internal const string Title = nameof(Title);
    }
}
