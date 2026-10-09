# Analyzer and code-fix patterns

## Analyzer lifetime and registration

An analyzer instance can serve multiple compilations. Keep compilation-derived state
out of analyzer fields, static fields, and global caches. Never retain a
`Compilation`, `ISymbol`, `SemanticModel`, operation, syntax node, or other
compilation-derived value there, including immutable collections of symbols or syntax
nodes: immutability does not stop those values from retaining a compilation or tree.
Only compilation-independent constants, such as registered syntax kinds, belong in
those fields. Initialize with `EnableConcurrentExecution()` and explicitly select
`ConfigureGeneratedCodeAnalysis(...)` flags to match the rule's intent.

Use a compilation-start callback to resolve required metadata types once. If a required
type is unavailable or ambiguous, do not register the inner analysis actions. Keep
per-compilation caches in that callback's closure or another compilation-scoped object,
including cached negative lookups. Do not perform repeated type lookup or semantic-model
work on a hot per-node path.

Prefer operation actions and `IOperation` when the rule concerns bound program behavior;
they avoid duplicating semantic analysis for each language. Use syntax actions where
source shape itself is the rule's subject. Filter cheaply before expensive work, use
the symbols carried by operations where possible, and compare symbols with
`SymbolEqualityComparer.Default` rather than names or display strings. Compare
`OriginalDefinition` only when generic construction is intentionally irrelevant.
Honor cancellation tokens in callbacks and long-running analysis.

Use `RegisterSymbolStartAction` / symbol-end analysis when state and the final decision
are scoped to one symbol. Use compilation-end analysis only when the rule genuinely
aggregates across the whole compilation; it delays IDE feedback. A diagnostic reported
from a compilation-end action must include the Roslyn
`WellKnownDiagnosticTags.CompilationEnd` tag on its descriptor. This is required
registration metadata, not a new diagnostic-ID or severity decision.

Report the narrowest accurate source location. Put per-report information needed by a
fix in `Diagnostic.Properties` or additional locations, not `CustomTags` (which describe
the rule rather than an individual diagnostic). Do not restrict a diagnostic to shapes
the fixer can rewrite: report the issue, then separately decide whether the current
syntax and semantics are fixable.

Choose severity under the reviewed rule policy and support it with false-positive
evidence from representative code; unit tests establish intended matches, not the
false-positive rate. A warning can break this repository's warnings-as-errors builds,
so do not escalate severity speculatively.

Descriptor titles, messages, descriptions, categories, defaults, and help links must
describe the reviewed rule. Reuse existing localization and descriptor helpers rather
than importing SDK-specific ones. If the project uses
`AnalyzerReleases.Unshipped.md` / `AnalyzerReleases.Shipped.md`, follow its existing
release-tracking conventions for new or changed diagnostics; do not disable the
corresponding meta-analyzer checks. A help link must resolve to documentation for that
rule, not a copied link to an unrelated diagnostic.

## Matching and performance

Pattern-match the operation shape and the data needed from it together rather than
testing `Kind` and then casting separately. A method name is a useful cheap filter, not
proof of symbol identity. Check the resolved containing type/member as required by the
rule.

Invocation arguments are not necessarily in parameter order. Named and reordered
arguments can put parameter zero somewhere other than `Arguments[0]`; match
`IArgumentOperation.Parameter` by its symbol or ordinal on the bound target instead.
For example, finding the argument bound to its first parameter:

```csharp
IArgumentOperation? firstArgument = null;

foreach (IArgumentOperation argument in invocation.Arguments)
{
    if (argument.Parameter is IParameterSymbol { Ordinal: 0 })
    {
        firstArgument = argument;
        break;
    }
}
```

Validate the required argument shape before using it, including error-recovery cases.
Account for optional/default arguments, `params`, reduced extension methods, and
`ref`/`out` when relevant. Matching a parameter does not authorize reordering the
user's argument evaluation in a fix. Test named/reordered calls and a match nested
inside a larger expression, not just a standalone positional call.

Keep hot per-node callbacks allocation-conscious: avoid repeated LINQ queries and
allocating closures, perform the cheapest rejection first, and use semantic information
already carried by the operation. For a rule matching many library members, build a
compilation-scoped member-to-rule lookup once rather than probing every candidate set
for every invocation. Cache negative results as well as positive results. Use existing
pooled helpers only where available and appropriate, and release them on every path;
do not introduce a pooling framework merely for a small rule.

## Code-fix eligibility and edits

Register an action only after confirming the diagnostic's current syntax is a shape the
fix can handle. Binding may succeed on malformed or incomplete source, so validate
required arguments and structure rather than relying on a non-null symbol or a debug
assertion. Re-find and revalidate nodes when applying a fix because the document may
have changed since the diagnostic was produced. Do not register an action that returns
the original document unchanged for a known-unfixable case.

Export the provider with `[ExportCodeFixProvider(...)]` for only the languages it
supports, mark it `[Shared]`, and use a stable, name-based equivalence key such as
`nameof(FixAction)`. Use the target project's existing localization resources or helpers
for diagnostic and action text when present. The fix title names the action, not the
problem: "Extract to a static readonly field", rather than repeating the diagnostic.
Keep the analyzer and
compiler assemblies free of Workspaces dependencies; reference those APIs only from the
actual code-fix project. Link the shared source files
`tools\Shared\CodeAnalysis\SyntaxEditorBasedCodeFixProvider.cs` and
`tools\Shared\CodeAnalysis\SyntaxEditorFixAllProvider.cs` into that project rather than
copying them. These shared types use the `TerraFX.CodeAnalysis` namespace; preserve the
shared implementation as the single source of truth.

`SyntaxEditorBasedCodeFixProvider` seals its fix-all override and fits one uniform action.
When one diagnostic ID has multiple actions or action eligibility varies by diagnostic,
keep registration conditional and use the stateful `SyntaxEditorFixAllProvider.Create`
overload or a custom provider. Filter by `FixAllContext.CodeActionEquivalenceKey`;
do not make the sealed base appear to support action selection it does not expose.

Build changes with `SyntaxEditor`, carrying trivia explicitly when replacing nodes.
Parenthesize expressions placed into arbitrary contexts and use simplifier annotations
when appropriate; preserve evaluation order, overload resolution, nullable behavior,
overflow, and other relevant semantics. If a useful fix unavoidably changes semantics,
say so in the action title and offer a semantics-preserving alternative when both
interpretations are reasonable.

Produce valid syntax for the supported rewrite, but do not withhold a useful fix merely
because the original document has unrelated compiler errors or requires further user
edits. The eligibility check must distinguish an unsupported rewrite from an otherwise
useful fix in an incomplete document.

## Fix-all behavior

The batch fixer applies each fix to the original document and merges text changes.
That is unsafe when edits overlap, target nested syntax, rewrite the same node, or add
items at the same position. For those shapes, apply all document diagnostics through
one `SyntaxEditor` using the shared base/provider where appropriate.

Preserve the shared `SyntaxEditorFixAllProvider` ordering: when `HasNestedSpans`
detects no nesting, diagnostics are ordered by ascending `Location.SourceSpan.Start`.
When it detects nesting, it switches the whole batch to descending start and ascending
end, so an inner span is handled before its outer span and shorter spans with the same
start come first. Do not impose descending order on every batch. In either case, use
the `ReplaceNode` lambda overload so an outer rewrite sees changes already made to
nested nodes. The shared editor-based provider supports Document, Project, and Solution
fix-all by applying independent document edits in the selected scope, including across
multiple documents. It does not coordinate cross-document semantic or name changes;
those require a provider designed for that operation.

Test nested, overlapping, and same-position diagnostics when the rule can produce
them. For a fixer, exercise a real single fix and Document, Project, and Solution
fix-all through the harness; include multiple-document cases for project and solution
scope. Assert that unsupported diagnostics have no registered action. Use
`NumberOfFixAllIterations = 2` as a positive control only against a known one-pass
fixture when establishing that the harness actually iterates; it is not a requirement
for every rule. Do not treat a passing multi-diagnostic assertion alone as proof, and
distinguish iteration-count failures from output mismatches.

Retained node references can become stale even within a syntax-list rewrite.
Chaining `SeparatedSyntaxList.Remove(originalNode)` calls can remove the first item
and silently miss later items because the returned list contains re-created nodes.
Resolve removal indices against the original list and remove them in descending order,
or reconstruct the list from its retained elements. Test multiple removals together,
not only one-node cases.

## Analyzer and code-fix tests

Use NUnit tests through the repository's VSTest setup. If a Roslyn test harness is
needed, use `Microsoft.CodeAnalysis.Testing` with the framework-agnostic
`DefaultVerifier`; do not assume its packages, wrapper types, or project are already
present. Keep package versions centrally managed.

Use a verifier alias rather than repeating the full harness type at each call site.
For an analyzer-only test, the shape is:

```csharp
using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerVerifier<
    ExampleAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
```

The following illustrates a realistic source fixture and mechanical NUnit data rows;
the example analyzer name and marked call are not a proposed TerraFX rule:

```csharp
[TestCase("int")]
[TestCase("string")]
public async Task Candidate_ReportsDiagnostic(string typeName)
{
    // lang=C#-test
    string source = $$"""
        using System;

        internal static class C
        {
            public static void M() => _ = [|Array.Empty<{{typeName}}>()|];
        }
        """;

    await VerifyCS.VerifyAnalyzerAsync(source);
}
```

`[|...|]` marks a report for a single-ID rule; use `{|EXAMPLE0001:...|}` when testing
multiple IDs. Assert message arguments and additional locations/properties explicitly
when they are part of the contract. Data rows should transform inputs/expectations
mechanically, not select different test behavior through branches in the test body.

When a fixer exists, use a corresponding alias:

```csharp
using VerifyFix = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    ExampleAnalyzer,
    ExampleCodeFixProvider,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;
```

Prefer paired `VerifyCodeFixAsync(markedSource, expectedSource)` tests that exercise
the diagnostic and rewrite together. An unchanged expected source is useful for
diagnostic-without-fix and no-diagnostic cases; do not separate coverage in a way that
leaves offered actions untested.

Set the tested language version and reference assemblies explicitly. Test every
language the provider declares, but do not add a language or a separate language test
project solely by convention. Cover positive and negative diagnostics, metadata passed
to the fix, and no-fix cases for reported but unsupported shapes. When a fixer exists,
test it together with the diagnostic; include comments and blank lines around rewritten
syntax. Add cases for invalid or incomplete code and, where relevant, nullable
annotations, overload changes, ref structs, and other contexts in which the rewrite
would not preserve meaning or compile. Use realistic compilable inputs as well as
intentional error-recovery cases. Raw string literals are useful for embedded C# test
sources, and data-driven cases help expose mechanical permutations without duplicating
test bodies.

Keep the harness's code-action validation strict. A `CodeActionValidationMode` failure
can mean the edited syntax tree differs from parsing its emitted text; repair the
rewrite instead of lowering validation, disabling fix-all, or accepting an extra pass.
Read failures carefully: an iteration-count failure is not a content mismatch.
Embedded source line endings must match the repository's LF convention; escaped `\n`
inside a verbatim string is literal text, not a newline.

## Representative-code validation

Run the built rule on a representative real consumer before selecting its enabled
severity, using the reviewed applicability and configuration. Triage every diagnostic,
including correct reports that would still be unreasonable to enforce. Record actual
counts and reproducible inputs, not just that a build completed. False positives require
fixing the match or revisiting the reviewed severity/opt-in policy; a green unit suite
does not justify an unconditional warning or error.
