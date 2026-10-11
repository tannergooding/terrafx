// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
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
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<TerraFX.Diagnostics.Analyzers.AutoImplementedPropertyLineAnalyzer, TerraFX.Diagnostics.Fixers.AutoImplementedPropertyLineCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCodeFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<TerraFX.Diagnostics.Analyzers.AutoImplementedPropertyLineAnalyzer, TerraFX.Diagnostics.Fixers.AutoImplementedPropertyLineCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace TerraFX.Tests;

[TestFixture]
internal sealed class AutoImplementedPropertyLineCodeFixTests
{
    private static async Task AssertNoCodeFixAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("AutoPropertyTest", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp12))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestUtilities.MetadataReferences);

        if (!workspace.TryApplyChanges(project.Solution))
        {
            throw new InvalidOperationException("The workspace rejected the auto-property test project.");
        }

        project = workspace.CurrentSolution.GetProject(project.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the auto-property test project.");
        var document = project.AddDocument("Test.cs", source);

        if (!workspace.TryApplyChanges(document.Project.Solution))
        {
            throw new InvalidOperationException("The workspace rejected the auto-property test document.");
        }

        document = workspace.CurrentSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the auto-property test document.");

        var diagnostics = await AnalyzerTestUtilities.AnalyzeAutoImplementedPropertyLineAsync(document).ConfigureAwait(false);
        var diagnostic = diagnostics.Single();
        var hasCodeFix = false;
        var context = new CodeFixContext(
            document,
            diagnostic,
            (CodeAction _, ImmutableArray<Diagnostic> _) => hasCodeFix = true,
            CancellationToken.None);

        await new AutoImplementedPropertyLineCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);

        Assert.That(hasCodeFix, Is.False);
    }

    [Test]
    public async Task CollapsesAutoImplementedPropertyAsync()
    {
        const string Source =
            """
            class C
            {
                public int Value
                {
                    get;
                    private set;
                }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                public int Value { get; private set; }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(AutoImplementedPropertyLineAnalyzer.DiagnosticId).WithSpan(3, 5, 7, 6));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task CollapsesAutoPropertyWithMultilineInitializerAsync()
    {
        const string Source =
            """
            class C
            {
                public int[] Values
                {
                    get;
                } = [
                    1,
                    2,
                ];
            }
            """;
        const string FixedSource =
            """
            class C
            {
                public int[] Values { get; } = [1, 2, ];
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(AutoImplementedPropertyLineAnalyzer.DiagnosticId).WithSpan(3, 5, 9, 7));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task DoesNotOfferFixWhenPropertyContainsCommentAsync()
    {
        const string Source =
            """
            class C
            {
                public int Value
                {
                    get /* Keep this comment in place. */;
                }
            }
            """;

        await AssertNoCodeFixAsync(Source).ConfigureAwait(false);
    }

    [Test]
    public async Task DoesNotOfferFixWhenPropertyContainsDirectiveAsync()
    {
        const string Source =
            """
            class C
            {
                public int Value
                {
            #if true
                    get;
            #endif
                }
            }
            """;

        await AssertNoCodeFixAsync(Source).ConfigureAwait(false);
    }

    [Test]
    public async Task PreservesLeadingCommentAsync()
    {
        const string Source =
            """
            class C
            {
                // Keep this comment with the property.
                public int Value
                {
                    get;
                }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                // Keep this comment with the property.
                public int Value { get; }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(AutoImplementedPropertyLineAnalyzer.DiagnosticId).WithSpan(4, 5, 7, 6));

        await test.RunAsync().ConfigureAwait(false);
    }

    private sealed class CSharp12CodeFixTest : CodeFixTest
    {
        protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose);
    }
}
