// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using TerraFX.Diagnostics.Analyzers;
using TerraFX.Diagnostics.Fixers;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<TerraFX.Diagnostics.Analyzers.DocumentationCommentLayoutAnalyzer, TerraFX.Diagnostics.Fixers.DocumentationCommentLayoutCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCodeFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<TerraFX.Diagnostics.Analyzers.DocumentationCommentLayoutAnalyzer, TerraFX.Diagnostics.Fixers.DocumentationCommentLayoutCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace TerraFX.Tests;

[TestFixture]
internal sealed class DocumentationCommentLayoutCodeFixTests
{
    [Test]
    public async Task AlignsContinuationMarkersInStructuredElementsAsync()
    {
        const string Source =
            """
            class C
            {
                /// <remarks>
                 /// <para>A structured paragraph.</para>
                /// </remarks>
                public void M() { }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                /// <remarks>
                /// <para>A structured paragraph.</para>
                /// </remarks>
                public void M() { }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(DocumentationCommentLayoutAnalyzer.DiagnosticId).WithSpan(3, 8, 6, 1));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task CollapsesMultilineSingleParagraphElementAsync()
    {
        const string Source =
            """
            class C
            {
                /// <summary>
                /// A concise summary.
                /// </summary>
                public void M() { }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                /// <summary>A concise summary.</summary>
                public void M() { }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(DocumentationCommentLayoutAnalyzer.DiagnosticId).WithSpan(3, 8, 6, 1));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task FixAllFormatsMultipleDocumentationCommentsAsync()
    {
        const string Source =
            """
            class First
            {
                /// <summary>
                /// First summary.
                /// </summary>
                public void M() { }
            }

            class Second
            {
                /// <summary>
                /// Second summary.
                /// </summary>
                public void M() { }
            }
            """;
        const string FixedSource =
            """
            class First
            {
                /// <summary>First summary.</summary>
                public void M() { }
            }

            class Second
            {
                /// <summary>Second summary.</summary>
                public void M() { }
            }
            """;

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("DocumentationLayoutTest", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestUtilities.MetadataReferences);

        if (!workspace.TryApplyChanges(project.Solution))
        {
            throw new InvalidOperationException("The workspace rejected the documentation-layout test project.");
        }

        project = workspace.CurrentSolution.GetProject(project.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the documentation-layout test project.");
        var document = project.AddDocument("Test.cs", Source);

        if (!workspace.TryApplyChanges(document.Project.Solution))
        {
            throw new InvalidOperationException("The workspace rejected the documentation-layout test document.");
        }

        document = workspace.CurrentSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the documentation-layout test document.");
        var diagnostics = await AnalyzerTestUtilities.AnalyzeDocumentationCommentLayoutAsync(document).ConfigureAwait(false);
        Assert.That(diagnostics, Has.Length.EqualTo(2));

        var provider = new DocumentationCommentLayoutCodeFixProvider();
        var fixAllContext = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            "ApplyFixAsync",
            provider.FixableDiagnosticIds,
            new DocumentationLayoutDiagnosticProvider(diagnostics),
            CancellationToken.None);
        var fixAllAction = await provider.GetFixAllProvider()
            .GetFixAsync(fixAllContext)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The documentation-layout code fix did not provide a fix-all action.");
        var operations = await fixAllAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var applyChanges = operations.OfType<ApplyChangesOperation>().Single();
        var fixedDocument = applyChanges.ChangedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The fix-all operation did not preserve the test document.");

        Assert.That((await fixedDocument.GetTextAsync().ConfigureAwait(false)).ToString(), Is.EqualTo(FixedSource));
        Assert.That(await AnalyzerTestUtilities.AnalyzeDocumentationCommentLayoutAsync(fixedDocument).ConfigureAwait(false), Is.Empty);
    }

    [Test]
    public async Task PreservesBlankParagraphBreaksAsync()
    {
        const string Source =
            """
            class C
            {
                /// <summary>
                /// First paragraph.
                ///
                /// Second paragraph.
                /// </summary>
                public void M() { }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = Source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task PreservesInlineXmlMarkupAsync()
    {
        const string Source =
            """
            class C
            {
                /// <summary>
                /// Returns the value from
                /// <see cref="C.Value"/>.
                /// </summary>
                public int Value => 0;
            }
            """;
        const string FixedSource =
            """
            class C
            {
                /// <summary>Returns the value from <see cref="C.Value"/>.</summary>
                public int Value => 0;
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(DocumentationCommentLayoutAnalyzer.DiagnosticId).WithSpan(3, 8, 7, 1));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task PreservesStructuredMultilineElementsAsync()
    {
        const string Source =
            """
            class C
            {
                /// <summary>
                /// A summary with separate paragraphs.
                /// <para>Second paragraph.</para>
                /// </summary>
                public void M() { }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = Source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        await test.RunAsync().ConfigureAwait(false);
    }

    private sealed class CSharp12CodeFixTest : CodeFixTest
    {
        protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose);
    }

    private sealed class DocumentationLayoutDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        private readonly ImmutableArray<Diagnostic> _diagnostics;

        internal DocumentationLayoutDiagnosticProvider(ImmutableArray<Diagnostic> diagnostics)
        {
            _diagnostics = diagnostics;
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IEnumerable<Diagnostic>>(Array.Empty<Diagnostic>());
        }
    }
}
