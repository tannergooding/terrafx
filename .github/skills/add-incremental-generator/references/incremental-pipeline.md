# Incremental pipeline and test patterns

## Define a useful pipeline

Implement `IIncrementalGenerator`; do not begin with a classic `ISourceGenerator`
`Execute` scaffold. `Initialize` defines the immutable pipeline, so avoid mutable
generator-instance or global state. Use static predicates, transforms, and output
callbacks wherever possible, and honor their cancellation tokens during parsing,
semantic analysis, and generation.

Generators add source; they do not rewrite user syntax. Ordinary generator output is
unordered and is not a dependable input to another generator or to a later stage of
the same pipeline. Do not rely on registration order, another generator's output, or
`CompilationProvider` containing source produced during the current generator run.
Treat pre-compilation generation as an optional extension only after explicitly
reviewing the API support and design need; do not baseline experimental APIs or
warning suppressions.

For syntax-driven generation, use a cheap syntax predicate and
`ForAttributeWithMetadataName` when the feature has an approved marker attribute.
Avoid scans of every declaration or whole-compilation searches for inherited marker
types. Use post-initialization output for fixed source such as a marker attribute;
when an internal marker is emitted, use `Microsoft.CodeAnalysis.EmbeddedAttribute`
where the referenced Roslyn APIs support it.

## Preserve incrementality with value models

Symbols, compilations, semantic models, and syntax nodes are compiler-owned inputs,
not suitable long-lived output models. In the transform that uses them, project the
information needed by later steps into a small immutable model of strings, booleans,
numbers, enums, and other value data. Do not retain `ISymbol`, `Compilation`,
`SemanticModel`, or `SyntaxNode` in the model. Keep a `Location` or syntax node only
on a diagnostic path that needs it, and separate it from source-generation models
as soon as possible.

Records are one possible implementation, not a requirement. Check language and
runtime support for the generator target before relying on record features or their
generated members, especially for `netstandard2.0`. Ensure equality and hashing
include every value that can affect output. Arrays and many immutable collection
types do not provide element-wise equality; provide a justified sequence comparer or
small collection wrapper where needed. Do not write a comparer that makes different
required output appear equal.

Keep output per item so an unrelated input does not invalidate every generated
file. Use late, narrow `Combine` operations and `Collect` only when a particular
output genuinely needs aggregation. Sort aggregate inputs by a stable, explicit
key before emission. Do not combine a broad compilation value with each candidate
unless every candidate's output actually depends on it.

Read all external inputs from the corresponding providers: source/parse options,
`AdditionalTextsProvider`, `AnalyzerConfigOptionsProvider`, and other declared
Roslyn inputs. Expose MSBuild properties or item metadata through the repository's
existing declared compiler-visible configuration. Never discover inputs by reading
the filesystem or process environment inside a generator callback.

## Emit stable source and useful diagnostics

Give each output a stable, collision-free hint name based on its complete logical
identity, not just a potentially duplicated short file or type name. For aggregate
output, use deterministic ordering. Escape identifiers and string literals with
language-aware logic; use globally qualified type names and invariant formatting.
Respect the consumer's parse options, supported language version, framework APIs,
and nullable context. Do not derive schema identities or tags from reflected CLR
members or property order.

Emit source text with UTF-8 encoding and the project's generated-file header
convention. Prefer a small text writer or `StringBuilder` to constructing syntax
trees solely for generation and normalizing them with `NormalizeWhitespace`.
Check the generated text in a real compilation, including namespaced and nested
types, nullable input, escaping, and the minimum supported language/framework
configuration relevant to the feature.

Report a useful diagnostic when invalid input prevents safe generation, using only
reviewed diagnostic metadata and identifiers. Do not silently emit malformed or
semantically misleading source. Because generator diagnostics have IDE and
incremental limitations, use a companion analyzer for prompt validation where that
is the appropriate user experience; do not add one speculatively.

## Generator tests

Use NUnit through the existing VSTest setup, and manage any new package versions
through `Directory.Packages.props`. Roslyn's cookbook recommends
`Microsoft.CodeAnalysis.Testing`, including
`Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing`, for generated-source and
diagnostic assertions. Its framework-agnostic `DefaultVerifier` works with NUnit;
no runner replacement is needed. Follow the installed harness's supported
incremental-generator adapter rather than changing the implementation to a classic
generator just to satisfy a test type constraint.

Direct generator-driver tests are also useful, particularly for cache behavior and
tracked steps. Reuse the existing harness for the assertions it already provides
instead of rebuilding it manually. A feature may use the harness for source/diagnostic
coverage and direct driver tests for incrementality; neither approach is universally
required to replace the other.

Compile the generated output and assert compiler/generator diagnostics, not only a
source snapshot or byte string. Set parse options, language version, and metadata
references explicitly. Use snapshots as a focused output assertion when helpful, not
as a substitute for compilation or incremental checks.

### Incremental-cache observations

To prove caching behavior, assign meaningful `WithTrackingName(...)` values to
important pipeline steps and enable incremental-step tracking on the driver. Retain
the **updated** driver returned from `RunGeneratorsAndUpdateCompilation` after every
run; reusing the original driver discards the state the test is meant to inspect.
For the same scenario, test:

- a no-op rerun and an unrelated source edit, which should reuse unaffected work;
- a relevant source edit, which should recompute only affected steps and output;
- adding and removing a candidate or source input;
- changing analyzer-config/MSBuild input and changing additional-file content or
  membership.

Assert tracked run reasons as well as generated behavior so a test cannot pass just
because the output text happened to remain identical. Keep the tests aligned with
the actual dependency graph: a step that depends on a changed input should rerun,
while independent items should remain cached.

When generated code feeds a NativeAOT scenario with selected closed roots, generator
unit tests and JIT compilation are not the final proof. At the later integration
boundary, publish and execute the actual artifact for each selected root.
