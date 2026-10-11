// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using TerraFX.Diagnostics.Analyzers;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<TerraFX.Diagnostics.Analyzers.MemberOrderingAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<TerraFX.Diagnostics.Analyzers.MemberOrderingAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace TerraFX.Tests;

[TestFixture]
internal sealed class MemberOrderingAnalyzerTests
{
    private static AnalyzerTest CreateTest(string source)
    {
        var test = new CSharp12AnalyzerTest {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        return test;
    }

    [Test]
    public async Task AcceptsMembersInCanonicalOrderAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                public static int AlphaValue { get; }
                public int BravoValue { get; }
                public static void Alpha(int value) { }
                public void Alpha() { }
                public void Bravo() { }
                private void Zulu() { }
            }
            """);

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task AcceptsMembersInOrdinalNameOrderAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                public void Zebra() { }
                public void apple() { }
            }
            """);

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task DoesNotReorderMembersAcrossInitializersAsync()
    {
        var test = CreateTest(
            """
            class {|#0:C|}
            {
                private readonly int _zulu = CreateZulu();
                private readonly int _alpha = CreateAlpha();
                private static int CreateZulu() => 1;
                private static int CreateAlpha() => 2;
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(MemberOrderingAnalyzer.DiagnosticId).WithLocation(0));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMembersOutsideCanonicalOrderAsync()
    {
        var test = CreateTest(
            """
            class {|#0:C|}
            {
                private void Zulu() { }
                public void Bravo() { }
                public static void Alpha(int value) { }
                public void Alpha() { }
                public int Value { get; }
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(MemberOrderingAnalyzer.DiagnosticId).WithLocation(0));

        await test.RunAsync().ConfigureAwait(false);
    }

    private sealed class CSharp12AnalyzerTest : AnalyzerTest
    {
        protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose);
    }
}
