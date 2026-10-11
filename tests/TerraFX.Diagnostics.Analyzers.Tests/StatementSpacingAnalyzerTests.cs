// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using TerraFX.Diagnostics.Analyzers;
using AnalyzerTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<TerraFX.Diagnostics.Analyzers.StatementSpacingAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<TerraFX.Diagnostics.Analyzers.StatementSpacingAnalyzer, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace TerraFX.Tests;

[TestFixture]
internal sealed class StatementSpacingAnalyzerTests
{
    private static AnalyzerTest CreateTest(string source)
    {
        return new CSharp12AnalyzerTest {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
    }

    [Test]
    public async Task AcceptsAdjacentFieldDeclarationsAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                private int _first;
                private int _second;
            }
            """);

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task AcceptsBlankLinesAndCommentsBetweenSectionsAsync()
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
                        {
                            value++;
                            break;
                        }

                        // The comment remains separated from the preceding section.
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
    public async Task AcceptsContinuationClausesAndNestedClosingBracesAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                void M(int value)
                {
                    if (value == 0)
                    {
                        value++;
                    }
                    else if (value == 1)
                    {
                        value++;
                    }
                    else
                    {
                        value++;
                    }

                    do
                    {
                        value--;
                    }
                    while (value > 0);
                    {
                        {
                            value++;
                        }
                    }
                }
            }
            """);

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task AcceptsSeparatedMemberDeclarationsAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                void M()
                {
                }

                void N()
                {
                }
            }
            """);

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsBlankLineAfterTypeOpeningBraceAsync()
    {
        var test = CreateTest(
            """
            class C
            {

                void M()
                {
                }
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(4, 5, 4, 9));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMissingBlankLineAfterBlockStatementAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                void M(bool value)
                {
                    if (value)
                    {
                        value = false;
                    }
                    value = true;
                }
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(9, 9, 9, 14));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMissingBlankLineBeforeIfTryAndWhileAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                void M(int value)
                {
                    value++;
                    if (value > 0)
                    {
                        value--;
                    }
                    value++;
                    try
                    {
                        value--;
                    }
                    catch
                    {
                    }
                    value++;
                    while (value > 0)
                    {
                        value--;
                    }
                }
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(6, 9, 6, 11));
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(10, 9, 10, 14));
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(11, 9, 11, 12));
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(18, 9, 18, 14));
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(19, 9, 19, 14));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMissingBlankLineBeforeSectionCommentAsync()
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
                        {
                            value++;
                            break;
                        }
                        // Separate the next section from the previous body.
                        default:
                        {
                            break;
                        }
                    }
                }
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(13, 13, 13, 21));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMissingBlankLineBeforeTopLevelControlFlowAsync()
    {
        var test = CreateTest(
            """
            System.Console.WriteLine();
            if (true)
            {
            }
            """);

        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(2, 1, 2, 3));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMissingBlankLineBetweenSectionsAsync()
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

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(12, 13, 12, 21));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMissingBlankLineBetweenTopLevelStatementsAsync()
    {
        var test = CreateTest(
            """
            if (true)
            {
            }
            System.Console.WriteLine();
            """);

        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(4, 1, 4, 7));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMissingBlankLineBetweenTypeMembersAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                void M()
                {
                }
                void N()
                {
                }
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(6, 5, 6, 9));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ReportsMissingBlankLinesBeforeSwitchAndIterationStatementsAsync()
    {
        var test = CreateTest(
            """
            class C
            {
                void M(int value)
                {
                    value++;
                    switch (value)
                    {
                    }
                    value++;
                    for (var index = 0; index < 1; index++)
                    {
                    }
                    value++;
                    foreach (var item in new[] { 1 })
                    {
                    }
                }
            }
            """);

        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(6, 9, 6, 15));
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(9, 9, 9, 14));
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(10, 9, 10, 12));
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(13, 9, 13, 14));
        test.ExpectedDiagnostics.Add(VerifyCS.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(14, 9, 14, 16));

        await test.RunAsync().ConfigureAwait(false);
    }

    private sealed class CSharp12AnalyzerTest : AnalyzerTest
    {
        protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose);
    }
}
