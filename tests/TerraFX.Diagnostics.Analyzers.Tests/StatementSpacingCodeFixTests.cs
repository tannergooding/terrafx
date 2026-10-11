// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using NUnit.Framework;
using TerraFX.Diagnostics.Analyzers;
using CodeFixTest = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixTest<TerraFX.Diagnostics.Analyzers.StatementSpacingAnalyzer, TerraFX.Diagnostics.Fixers.StatementSpacingCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
using VerifyCodeFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<TerraFX.Diagnostics.Analyzers.StatementSpacingAnalyzer, TerraFX.Diagnostics.Fixers.StatementSpacingCodeFixProvider, Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace TerraFX.Tests;

[TestFixture]
internal sealed class StatementSpacingCodeFixTests
{
    [Test]
    public async Task InsertsBlankLineAfterSwitchSectionAsync()
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

                        default:
                        {
                            break;
                        }
                    }
                }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(12, 13, 12, 21));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task InsertsBlankLineBeforeIfAsync()
    {
        const string Source =
            """
            class C
            {
                void M(bool value)
                {
                    value = false;
                    if (value)
                    {
                        value = true;
                    }
                }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                void M(bool value)
                {
                    value = false;

                    if (value)
                    {
                        value = true;
                    }
                }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(6, 9, 6, 11));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task InsertsBlankLineBetweenBlockStatementsAsync()
    {
        const string Source =
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
            """;
        const string FixedSource =
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
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(9, 9, 9, 14));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task InsertsBlankLineBetweenBraceTerminatedMembersAsync()
    {
        const string Source =
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
            """;
        const string FixedSource =
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
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(6, 5, 6, 9));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task InsertsBlankLineBetweenSameLineStatementsAsync()
    {
        const string Source =
            """
            class C
            {
                void M(bool value)
                {
                    if (value) { value = false; } value = true;
                }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                void M(bool value)
                {
                    if (value) { value = false; }

                    value = true;
                }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(5, 39, 5, 44));

        await test.RunAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task RemovesBlankLineAfterTypeOpeningBraceAsync()
    {
        const string Source =
            """
            class C
            {

                // Keep this comment with the first member.
                void M()
                {
                }
            }
            """;
        const string FixedSource =
            """
            class C
            {
                // Keep this comment with the first member.
                void M()
                {
                }
            }
            """;

        var test = new CSharp12CodeFixTest {
            TestCode = Source,
            FixedCode = FixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(VerifyCodeFix.Diagnostic(StatementSpacingAnalyzer.DiagnosticId).WithSpan(5, 5, 5, 9));

        await test.RunAsync().ConfigureAwait(false);
    }

    private sealed class CSharp12CodeFixTest : CodeFixTest
    {
        protected override ParseOptions CreateParseOptions()
            => new CSharpParseOptions(LanguageVersion.CSharp12, DocumentationMode.Diagnose);
    }
}
