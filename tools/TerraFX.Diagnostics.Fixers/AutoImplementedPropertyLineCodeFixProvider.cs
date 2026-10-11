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
using TerraFX.CodeAnalysis;
using TerraFX.Diagnostics.Analyzers;

namespace TerraFX.Diagnostics.Fixers;

/// <summary>
/// Provides a fix that places an auto-implemented property on one line.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AutoImplementedPropertyLineCodeFixProvider)), Shared]
public sealed class AutoImplementedPropertyLineCodeFixProvider : SyntaxEditorBasedCodeFixProvider
{
    private static readonly ResourceManager s_resourceManager = new ResourceManager("TerraFX.Diagnostics.Fixers.Resources", typeof(AutoImplementedPropertyLineCodeFixProvider).Assembly);

    private static string MakeAutoPropertySingleLine
    {
        get
        {
            var title = s_resourceManager.GetString(nameof(MakeAutoPropertySingleLine), CultureInfo.CurrentUICulture);

            if (title is null)
            {
                ThrowHelper.ThrowMissingManifestResourceException("The code-fix title resource is missing.");
            }

            return title;
        }
    }

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [AutoImplementedPropertyLineAnalyzer.DiagnosticId];

    private static bool IsSafeToRewrite(PropertyDeclarationSyntax property)
    {
        foreach (var trivia in property.DescendantTrivia(descendIntoTrivia: true))
        {
            if ((trivia.FullSpan.Start >= property.SpanStart)
                && (trivia.FullSpan.End <= property.Span.End)
                && !trivia.IsKind(SyntaxKind.WhitespaceTrivia)
                && !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                return false;
            }
        }

        return true;
    }

    private static PropertyDeclarationSyntax? TryGetFixableProperty(SyntaxNode root, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree != root.SyntaxTree)
        {
            return null;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var property = node.FirstAncestorOrSelf<PropertyDeclarationSyntax>();

        if (property is null)
        {
            return null;
        }

        var accessors = property.AccessorList?.Accessors;

        if ((accessors is null) || (accessors.Value.Count == 0) || (property.ExpressionBody is not null))
        {
            return null;
        }

        foreach (var accessor in accessors.Value)
        {
            if ((accessor.Body is not null)
                || (accessor.ExpressionBody is not null)
                || accessor.SemicolonToken.IsMissing)
            {
                return null;
            }
        }

        return IsSafeToRewrite(property) ? property : null;
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
            if (TryGetFixableProperty(root, diagnostic) is not null)
            {
                RegisterCodeFix(context, MakeAutoPropertySingleLine, nameof(ApplyFixAsync));
                return;
            }
        }
    }

    /// <inheritdoc/>
    protected override Task ApplyFixAsync(Document document, Diagnostic diagnostic, SyntaxEditor editor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var property = TryGetFixableProperty(editor.OriginalRoot, diagnostic);

        if (property is null)
        {
            return Task.CompletedTask;
        }

        var leadingTrivia = property.GetLeadingTrivia();
        var trailingTrivia = property.GetTrailingTrivia();
        var normalizedProperty = property
            .WithoutLeadingTrivia()
            .WithoutTrailingTrivia()
            .NormalizeWhitespace(" ", " ")
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(trailingTrivia);

        editor.ReplaceNode(property, normalizedProperty);
        return Task.CompletedTask;
    }
}
