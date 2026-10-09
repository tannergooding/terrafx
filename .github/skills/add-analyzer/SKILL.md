---
name: add-analyzer
description: >
  Add or revise an approved Roslyn diagnostic analyzer and optional code fix in
  TerraFX. USE FOR: analyzer-rule design, implementation, or NUnit tests.
  DO NOT USE FOR: incremental generators, compiler diagnostics, or speculative
  rules without a concrete consumer.
license: MIT
---

# Add an analyzer

Use this workflow only for a concrete rule with an identified consumer. Inspect the
current projects and tests rather than assuming an analyzer project, code-fix project,
test harness, or shared helper already exists.

1. **Confirm the rule and its owner.** Agree on the rule's intended behavior, the
   supported project and languages, generated-code handling, whether a fix is wanted,
   and the repository-approved diagnostic ID and severity policy. Base severity on
   reviewed policy and false-positive evidence; warnings can break this repository's
   warnings-as-errors builds, so do not escalate speculatively. Decide whether this is
   product tooling for consumers or repository-only analysis: put the former in the
   appropriate consumer-facing project and the latter under `tools` without packaging
   it. Do not invent IDs, severity policy, schema/API contracts, or an empty analyzer
   project to host speculative work.
2. **Inspect the implementation and test surface.** Follow the target project's Roslyn
   references and test conventions. Package identities and versions belong in the
   existing `Directory.Packages.props`; do not add an independent version pin or a
   custom test runner. Reuse existing localization resources or helpers where present
   rather than adding a new resource pipeline. Read
   [the analyzer and code-fix patterns](references/authoring-patterns.md) before
   implementing the analyzer, fixer, or tests. Follow existing descriptor and release
   tracking requirements when the target project uses them; do not suppress the
   Roslyn meta-analyzers to avoid recording a new or changed rule.
3. **Implement the analyzer without retaining compilation state.** Choose concurrency
   and generated-code flags explicitly, resolve required metadata types once per
   compilation, and keep compiler objects in compilation-scoped callbacks only. Prefer
   `IOperation` and symbol identity for semantic rules; keep reporting independent from
   whether a code fix can handle a particular report.
4. **Add a real code fix only when its input is supported.** Keep Workspaces-dependent
   code in the actual code-fix project, separate from analyzer/compiler assemblies.
   Link `tools\Shared\CodeAnalysis\SyntaxEditorBasedCodeFixProvider.cs` and
   `tools\Shared\CodeAnalysis\SyntaxEditorFixAllProvider.cs` into that project when
   implementing a fixer; do not duplicate them or add Workspaces dependencies to the
   analyzer assembly. Preserve syntax trivia, precedence, and behavior, and do not offer
   an action that is known to be a no-op.
5. **Test the contract, not just the match.** Use the repository's NUnit/VSTest setup.
   When a Roslyn testing harness is needed, use its framework-agnostic
   `DefaultVerifier`; first confirm the harness and required packages exist, and add
   dependencies through central package management. Set language and reference versions
   explicitly. Cover diagnostics, single fixes, and no-fix cases, including no
   registered action for unsupported shapes. Exercise Document, Project, and Solution
   fix-all, including multiple documents, plus invalid or stale syntax
   and relevant trivia, nullable, overload, and ref-struct scenarios.
6. **Validate with normal .NET commands.** Build the owning project and run its tests
   with `dotnet build` and `dotnet test`, Debug before Release. Use solution-level
   validation only when the change affects integration beyond the owning projects.
7. **Validate matches on representative code.** Run the built analyzer against real
   intended consumers and triage every report before proposing an enabled severity.
   Record the consumer revision, configuration, diagnostic counts, and false-positive
   findings so the result is reproducible. Unit tests do not establish the
   false-positive rate. Use an ordinary analyzer reference or the existing integration
   setup; do not replace an installed SDK or modify the NuGet cache.
