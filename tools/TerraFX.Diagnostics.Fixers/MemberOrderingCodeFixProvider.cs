// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using TerraFX.CodeAnalysis;
using TerraFX.Diagnostics.Analyzers;

namespace TerraFX.Diagnostics.Fixers;

/// <summary>
/// Provides a fix that orders members within a type.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MemberOrderingCodeFixProvider)), Shared]
public sealed class MemberOrderingCodeFixProvider : SyntaxEditorBasedCodeFixProvider
{
    private static readonly ResourceManager s_resourceManager = new ResourceManager("TerraFX.Diagnostics.Fixers.Resources", typeof(MemberOrderingCodeFixProvider).Assembly);

    private static string FixMemberOrdering
    {
        get
        {
            var title = s_resourceManager.GetString(nameof(FixMemberOrdering), CultureInfo.CurrentUICulture);

            if (title is null)
            {
                ThrowHelper.ThrowMissingManifestResourceException("The code-fix title resource is missing.");
            }

            return title;
        }
    }

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds => [MemberOrderingAnalyzer.DiagnosticId];

    private static TypeDeclarationSyntax? TryGetFixableType(SyntaxNode root, Diagnostic diagnostic)
    {
        if (diagnostic.Location.SourceTree != root.SyntaxTree)
        {
            return null;
        }

        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var type = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();

        return type is not null && !MemberOrdering.IsOrdered(type) ? type : null;
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
            if (TryGetFixableType(root, diagnostic) is not null)
            {
                RegisterCodeFix(context, FixMemberOrdering, nameof(ApplyFixAsync));
                return;
            }
        }
    }

    /// <inheritdoc/>
    protected override Task ApplyFixAsync(Document document, Diagnostic diagnostic, SyntaxEditor editor, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var type = TryGetFixableType(editor.OriginalRoot, diagnostic);

        if (type is null)
        {
            return Task.CompletedTask;
        }

        editor.ReplaceNode(
            type,
            (rewrittenNode, _) => {
                var rewrittenType = (TypeDeclarationSyntax)rewrittenNode;
                return rewrittenType
                    .WithMembers(MemberOrdering.GetOrderedMembers(rewrittenType))
                    .WithAdditionalAnnotations(Formatter.Annotation);
            });

        return Task.CompletedTask;
    }
}
