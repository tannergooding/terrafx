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
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<TerraFX.Diagnostics.Analyzers.MemberOrderingAnalyzer, TerraFX.Diagnostics.Fixers.MemberOrderingCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCodeFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<TerraFX.Diagnostics.Analyzers.MemberOrderingAnalyzer, TerraFX.Diagnostics.Fixers.MemberOrderingCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace TerraFX.Tests;

[TestFixture]
internal sealed class MemberOrderingCodeFixTests
{
    [Test]
    public async Task FixAllOrdersNestedTypesAsync()
    {
        const string Source =
            """
            class Outer
            {
                private void Zulu() { }
                private void Alpha() { }

                class Inner
                {
                    private void Zulu() { }
                    private void Alpha() { }
                }
            }
            """;

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("MemberOrderingTest", LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp12))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(AnalyzerTestUtilities.MetadataReferences);

        if (!workspace.TryApplyChanges(project.Solution))
        {
            throw new InvalidOperationException("The workspace rejected the member-ordering test project.");
        }

        project = workspace.CurrentSolution.GetProject(project.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the member-ordering test project.");
        var document = project.AddDocument("Test.cs", Source);

        if (!workspace.TryApplyChanges(document.Project.Solution))
        {
            throw new InvalidOperationException("The workspace rejected the member-ordering test document.");
        }

        document = workspace.CurrentSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The workspace did not preserve the member-ordering test document.");

        var diagnostics = await AnalyzerTestUtilities.AnalyzeMemberOrderingAsync(document).ConfigureAwait(false);
        Assert.That(diagnostics, Has.Length.EqualTo(2));

        var provider = new MemberOrderingCodeFixProvider();
        var fixAllContext = new FixAllContext(
            document,
            provider,
            FixAllScope.Document,
            nameof(FixAllOrdersNestedTypesAsync),
            provider.FixableDiagnosticIds,
            new MemberOrderingDiagnosticProvider(),
            CancellationToken.None);
        var action = await provider.GetFixAllProvider().GetFixAsync(fixAllContext).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The member-ordering fix-all provider did not return an action.");
        var operations = await action.GetOperationsAsync(CancellationToken.None).ConfigureAwait(false);
        var changedSolution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)
            ?? throw new InvalidOperationException("The member-ordering fix-all operation did not preserve the test document.");

        Assert.That(await AnalyzerTestUtilities.AnalyzeMemberOrderingAsync(changedDocument).ConfigureAwait(false), Is.Empty);
    }

    [Test]
    public async Task OrdersEverySupportedMemberCategoryAsync()
    {
        const string Source =
            """
            class {|#0:C|}
            {
                private void ZMethod() { }
                public int ZProperty { get; }
                private int this[string name] => 0;
                public static explicit operator string(C value) => string.Empty;
                public static C operator -(C left, C right) => left;
                private C(int value) { }
                ~C() { }
                private int _instanceField;
                private const int ZConstant = 0;
                public static int AStaticProperty { get; }
                public static void ZStaticMethod() { }
                static C() { _staticField = 0; }
                public C() { _instanceField = 0; }
                private static int _staticField;
                public int this[int index] => index;
                public static implicit operator int(C value) => 0;
                public static C operator +(C left, C right) => left;
                public const int AConstant = 0;
                public static int BStaticField;
                public int AInstanceField;
                public int AProperty { get; }
                internal void AMid() { }
                protected void BProtected() { }
                public void Alpha() { }
                internal static void BStaticInternal() { }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                public const int AConstant = 0;
                private const int ZConstant = 0;
                public static int BStaticField;
                private static int _staticField;
                public int AInstanceField;
                private int _instanceField;
                static C() { _staticField = 0; }
                public C() { _instanceField = 0; }
                private C(int value) { }
                ~C() { }
                public static int AStaticProperty { get; }
                public int AProperty { get; }
                public int ZProperty { get; }
                public int this[int index] => index;
                private int this[string name] => 0;
                public static explicit operator string(C value) => string.Empty;
                public static implicit operator int(C value) => 0;
                public static C operator +(C left, C right) => left;
                public static C operator -(C left, C right) => left;
                public static void ZStaticMethod() { }
                internal static void BStaticInternal() { }
                public void Alpha() { }
                internal void AMid() { }
                protected void BProtected() { }
                private void ZMethod() { }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(MemberOrderingAnalyzer.DiagnosticId).WithLocation(0));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task OrdersMembersByCategoryStaticnessAccessibilityAndNameAsync()
    {
        const string Source =
            """
            class {|#0:C|}
            {
                private void Zulu() { }
                public void Bravo() { }
                public static void Alpha(int value) { }
                public void Alpha() { }
                public int Value { get; }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                public int Value { get; }
                public static void Alpha(int value) { }
                public void Alpha() { }
                public void Bravo() { }
                private void Zulu() { }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(MemberOrderingAnalyzer.DiagnosticId).WithLocation(0));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task OrdersNamesUsingOrdinalComparisonAsync()
    {
        const string Source =
            """
            class {|#0:C|}
            {
                private void apple() { }
                private void Zebra() { }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                private void Zebra() { }
                private void apple() { }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(MemberOrderingAnalyzer.DiagnosticId).WithLocation(0));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task PreservesInitializerOrderWhileSortingMethodsAsync()
    {
        const string Source =
            """
            class {|#0:C|}
            {
                private readonly int _zulu = CreateZulu();
                private readonly int _alpha = CreateAlpha();
                private static int CreateZulu() => 1;
                private static int CreateAlpha() => 2;
            }
            """;
        const string FixedSource =
            """
            class C
            {
                private readonly int _zulu = CreateZulu();
                private readonly int _alpha = CreateAlpha();
                private static int CreateAlpha() => 2;
                private static int CreateZulu() => 1;
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(MemberOrderingAnalyzer.DiagnosticId).WithLocation(0));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task PreservesSequentialStructFieldOrderAsync()
    {
        const string Source =
            """
            struct {|#0:C|}
            {
                private int ZuluField;
                private int AlphaField;
                private void ZuluMethod() { }
                private void AlphaMethod() { }
            }
            """;
        const string FixedSource =
            """
            struct C
            {
                private int ZuluField;
                private int AlphaField;
                private void AlphaMethod() { }
                private void ZuluMethod() { }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(MemberOrderingAnalyzer.DiagnosticId).WithLocation(0));

        await test.RunAsync().ConfigureAwait(false);
    }

    private sealed class CSharp12CodeFixTest : CodeFixTest
    {
        protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose);
    }

    private sealed class MemberOrderingDiagnosticProvider : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.AddRange(await AnalyzerTestUtilities.AnalyzeMemberOrderingAsync(document).ConfigureAwait(false));
            }

            return diagnostics.ToImmutable();
        }

        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await AnalyzerTestUtilities.AnalyzeMemberOrderingAsync(document).ConfigureAwait(false);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Enumerable.Empty<Diagnostic>());
        }
    }
}
