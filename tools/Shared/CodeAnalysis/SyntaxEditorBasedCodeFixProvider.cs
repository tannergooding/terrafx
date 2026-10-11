// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

// This file includes code based on the SyntaxEditorBasedCodeFixProvider class from https://github.com/dotnet/sdk/.
// The original code is Copyright © .NET Foundation and Contributors. All rights reserved. Licensed under the MIT License (MIT).

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Editing;

namespace TerraFX.CodeAnalysis;

/// <summary>
/// A <see cref="CodeFixProvider"/> whose fix is expressed as edits into a <see cref="SyntaxEditor"/>, so
/// that a single invocation and a fix-all pass run the same code over the same editor.
/// </summary>
/// <remarks>
/// <para>
/// This mirrors Roslyn's own <c>SyntaxEditorBasedCodeFixProvider</c>, which is <see langword="internal"/>
/// and source-shared and so cannot be referenced from here.
/// </para>
/// <para>
/// It differs in one way: it applies one diagnostic per call rather than handing the fix the whole batch,
/// so that the loop and its ordering live in one place instead of in every fixer - which is what Roslyn
/// needs a second base class, <c>ForkingSyntaxEditorBasedCodeFixProvider&lt;T&gt;</c>, to do.
/// </para>
/// </remarks>
public abstract class SyntaxEditorBasedCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public sealed override FixAllProvider GetFixAllProvider()
        => SyntaxEditorFixAllProvider.Create(ApplyFixAsync);

    /// <summary>
    /// Applies the fix for a single diagnostic into <paramref name="editor"/>.
    /// </summary>
    /// <param name="document">The document containing the diagnostic.</param>
    /// <param name="diagnostic">The diagnostic being fixed.</param>
    /// <param name="editor">The editor collecting the document's edits.</param>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <remarks>
    /// Every diagnostic in the document is applied into one editor, which is not re-created between them,
    /// so <paramref name="diagnostic"/> locates against <see cref="SyntaxEditor.OriginalRoot"/> and
    /// <paramref name="document"/> is the document as it was before any of the fixes ran. A fix that
    /// encloses another diagnostic has to read the node as already rewritten, through the
    /// <see cref="SyntaxEditor.ReplaceNode(SyntaxNode, Func{SyntaxNode, SyntaxGenerator, SyntaxNode})"/>
    /// overload - see the remarks on <see cref="SyntaxEditorFixAllProvider"/>.
    /// </remarks>
    protected abstract Task ApplyFixAsync(Document document, Diagnostic diagnostic, SyntaxEditor editor, CancellationToken cancellationToken);

    /// <summary>
    /// Registers an action applying <see cref="ApplyFixAsync"/> to every diagnostic in
    /// <paramref name="context"/>, through the same editor a fix-all pass would use.
    /// </summary>
    /// <param name="context">The code-fix context containing the diagnostics to fix.</param>
    /// <param name="title">The action title shown to the user.</param>
    /// <param name="equivalenceKey">The stable key identifying this code action.</param>
    protected void RegisterCodeFix(CodeFixContext context, string title, string equivalenceKey)
    {
        ThrowHelper.ThrowIfNull(title, nameof(title));
        ThrowHelper.ThrowIfNull(equivalenceKey, nameof(equivalenceKey));

        var document = context.Document;
        var diagnostics = context.Diagnostics;

        var codeAction = CodeAction.Create(
            title,
            cancellationToken => SyntaxEditorFixAllProvider.ApplyFixesAsync(document, diagnostics, ApplyFixAsync, cancellationToken),
            equivalenceKey);
        context.RegisterCodeFix(codeAction, diagnostics);
    }
}
