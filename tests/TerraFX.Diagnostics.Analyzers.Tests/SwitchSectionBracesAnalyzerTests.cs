// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using TerraFX.Diagnostics.Analyzers;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<TerraFX.Diagnostics.Analyzers.SwitchSectionBracesAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<TerraFX.Diagnostics.Analyzers.SwitchSectionBracesAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace TerraFX.Tests;

[TestFixture]
internal sealed class SwitchSectionBracesAnalyzerTests
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
    public async Task AcceptsBracedSectionsAndGroupedLabelsAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 0:
                        case 1:
                        {
                            value++;
                            break;
                        }
                        default:
                        {
                            break;
                        }
                    }
                }
            }
            """);

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsUnbracedMultipleStatementSectionAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                void M(int value)
                {
                    switch (value)
                    {
                        case 0:
                            value++;
                            value--;
                            break;
                    }
                }
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(SwitchSectionBracesAnalyzer.DiagnosticId).WithSpan(8, 17, 10, 23));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsUnbracedSingleStatementSectionAsync()
    {
        var test = CreateTest(
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
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(SwitchSectionBracesAnalyzer.DiagnosticId).WithSpan(8, 17, 9, 23));

        await test.RunAsync().ConfigureAwait(false);
    }

    private sealed class CSharp12AnalyzerTest : AnalyzerTest
    {
        protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose);
    }
}
