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
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using TerraFX.Diagnostics.Analyzers;
using TerraFX.Diagnostics.Fixers;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<TerraFX.Diagnostics.Analyzers.SwitchSectionBracesAnalyzer, TerraFX.Diagnostics.Fixers.SwitchSectionBracesCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCodeFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<TerraFX.Diagnostics.Analyzers.SwitchSectionBracesAnalyzer, TerraFX.Diagnostics.Fixers.SwitchSectionBracesCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace TerraFX.Tests;

[TestFixture]
internal sealed class SwitchSectionBracesCodeFixTests
{
    private static (Project Project, Document Document) AddDocument(AdhocWorkspace workspace, Project project, string name, string source)
    {
        var document = project.AddDocument(name, source);

        if (!workspace.TryApplyChanges(document.Project.Solution))
        {
            throw new InvalidOperationException("The workspace rejected the test document.");
        }

        var currentSolution = workspace.CurrentSolution;
        var currentProject = currentSolution.GetProject(project.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the test project.");
        var currentDocument = currentSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the test document.");

        return (currentProject, currentDocument);
    }

    private static Project CreateProject(AdhocWorkspace workspace, string name)
    {
        var project = workspace.AddProject(name, LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp12))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestUtilities.MetadataReferences);

        if (!workspace.TryApplyChanges(project.Solution))
        {
            throw new InvalidOperationException("The workspace rejected the project configuration.");
        }

        return workspace.CurrentSolution.GetProject(project.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the test project.");
    }

    [Test]
    public async Task DoesNotRegisterCodeFixForStaleDiagnosticAsync()
    {
        const string Source =
            """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 0:
                            value++;
                            break;
                    }
                }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 0:
                        {
                            value++;
                            break;
                        }
                    }
                }
            }
            """;

        using var workspace = new AdhocWorkspace();
        var project = CreateProject(workspace, "AnalyzerTest");
        var document = project.AddDocument("Test.cs", Source);
        var diagnostic = (await AnalyzerTestUtilities.AnalyzeSwitchSectionBracesAsync(document).ConfigureAwait(false)).Single();

        var updatedDocument = document.WithText(SourceText.From(FixedSource));
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(
            updatedDocument,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new SwitchSectionBracesCodeFixProvider().RegisterCodeFixesAsync(context).ConfigureAwait(false);

        Assert.That(actions, Is.Empty);
    }

    [TestCase(FixAllScope.Document)]
    [TestCase(FixAllScope.Project)]
    [TestCase(FixAllScope.Solution)]
    public async Task FixAllScopesHandleNestedSectionsAsync(FixAllScope scope)
    {
        const string NestedSource =
            """
            class First
            {
                void M(int outer, int inner)
                {
                    switch (outer)
                    {
                        case 0:
                            switch (inner)
                            {
                                case 0:
                                    inner++;
                                    break;
                            }
                            break;
                        case 1:
                            outer++;
                            break;
                    }
                }
            }
            """;
        const string SecondSource =
            """
            class Second
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 0:
                            value++;
                            break;
                    }
                }
            }
            """;

        using var workspace = new AdhocWorkspace();
        var firstProject = CreateProject(workspace, "FirstProject");
        (firstProject, var firstDocument) = AddDocument(workspace, firstProject, "First.cs", NestedSource);
        (firstProject, var secondDocument) = AddDocument(workspace, firstProject, "Second.cs", SecondSource);
        var secondProject = CreateProject(workspace, "SecondProject");
        (secondProject, var thirdDocument) = AddDocument(workspace, secondProject, "Third.cs", SecondSource);
        var currentSolution = workspace.CurrentSolution;
        firstDocument = currentSolution.GetDocument(firstDocument.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the first test document.");
        secondDocument = currentSolution.GetDocument(secondDocument.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the second test document.");
        thirdDocument = currentSolution.GetDocument(thirdDocument.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the third test document.");
        var firstDocumentId = firstDocument.Id;
        var secondDocumentId = secondDocument.Id;
        var thirdDocumentId = thirdDocument.Id;

        Assert.That(await AnalyzerTestUtilities.AnalyzeSwitchSectionBracesAsync(firstDocument).ConfigureAwait(false), Has.Length.EqualTo(3));

        var codeFixProvider = new SwitchSectionBracesCodeFixProvider();
        var fixAllContext = new FixAllContext(
            firstDocument,
            codeFixProvider,
            scope,
            "ApplyFixAsync",
            codeFixProvider.FixableDiagnosticIds,
            new AnalyzerDiagnosticProvider(),
            CancellationToken.None);
        var fixAllAction = await codeFixProvider.GetFixAllProvider()
            .GetFixAsync(fixAllContext)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("The code fix did not provide a fix-all action.");
        var operations = await fixAllAction.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var applyChanges = operations.OfType<ApplyChangesOperation>().Single();
        var changedSolution = applyChanges.ChangedSolution;

        ImmutableArray<DocumentId> fixedDocumentIds = scope switch {
            FixAllScope.Document => [firstDocumentId],
            FixAllScope.Project => [firstDocumentId, secondDocumentId],
            FixAllScope.Solution => [firstDocumentId, secondDocumentId, thirdDocumentId],
            FixAllScope.Custom => throw new ArgumentOutOfRangeException(nameof(scope)),
            FixAllScope.ContainingMember => throw new ArgumentOutOfRangeException(nameof(scope)),
            FixAllScope.ContainingType => throw new ArgumentOutOfRangeException(nameof(scope)),
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };

        foreach (var documentId in fixedDocumentIds)
        {
            var document = changedSolution.GetDocument(documentId)
                ?? throw new InvalidOperationException($"The fix-all operation did not preserve test document '{documentId}' in projects [{string.Join(", ", changedSolution.ProjectIds)}].");
            Assert.That(await AnalyzerTestUtilities.AnalyzeSwitchSectionBracesAsync(document).ConfigureAwait(false), Is.Empty);
        }

        foreach (var documentId in new[] { firstDocumentId, secondDocumentId, thirdDocumentId }.Except(fixedDocumentIds))
        {
            var document = changedSolution.GetDocument(documentId)
                ?? throw new InvalidOperationException($"The fix-all operation did not preserve test document '{documentId}' in projects [{string.Join(", ", changedSolution.ProjectIds)}].");
            Assert.That(await AnalyzerTestUtilities.AnalyzeSwitchSectionBracesAsync(document).ConfigureAwait(false), Is.Not.Empty);
        }
    }

    [Test]
    public async Task WrapsSwitchSectionStatementsInBlockAsync()
    {
        const string Source =
            """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 0:
                            value++;
                            break;
                    }
                }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 0:
                            {
                                value++;
                                break;
                            }
                    }
                }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            // The formatter uses Windows newlines for the annotated block; the untouched fixture remains LF.
            FixedCode = FixedSource.Replace(
                "            case 0:\n                {\n                    value++;\n                    break;\n                }\n        }",
                "            case 0:\r\n                {\r\n                    value++;\r\n                    break;\r\n                }\r\n        }",
                StringComparison.Ordinal),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(SwitchSectionBracesAnalyzer.DiagnosticId).WithSpan(8, 17, 9, 23));

        await test.RunAsync().ConfigureAwait(false);
    }

    private sealed class AnalyzerDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.AddRange(await AnalyzerTestUtilities.AnalyzeSwitchSectionBracesAsync(document).ConfigureAwait(false));
            }

            return diagnostics.ToImmutable();
        }

        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await AnalyzerTestUtilities.AnalyzeSwitchSectionBracesAsync(document).ConfigureAwait(false);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Enumerable.Empty<Diagnostic>());
        }
    }

    private sealed class CSharp12CodeFixTest : CodeFixTest
    {
        protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose);
    }
}
