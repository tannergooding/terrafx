---
name: add-incremental-generator
description: >
  Design, implement, or test a TerraFX Roslyn incremental source generator.
  USE FOR: a concrete feature that must add generated source to a consumer
  compilation. DO NOT USE FOR: analyzers, code rewriting, or speculative
  generator scaffolding without a real consumer.
license: MIT
---

# Add an incremental generator

Use this workflow for a concrete source-generation feature with an identified
consumer, not to populate the generator project speculatively. Read the current
Roslyn [incremental-generator design](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)
and [cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.cookbook.md),
then use the focused
[TerraFX pipeline and testing guidance](references/incremental-pipeline.md).

1. **Confirm generation is the right mechanism.** Agree on the consumer, generated
   contract, inputs, supported language/framework range, and behavior for invalid
   input. Source generators are additive: they do not rewrite user code, and ordinary
   generators cannot rely on another generator's order or generated output being
   visible as input. If generation carries TerraFX schema or wire data, use reviewed
   explicit descriptors and registrations; never infer identities or tags from CLR
   names, reflection, or property order.
2. **Inspect the existing project boundary.** Use the actual generator and test
   projects and their current Roslyn API references. Respect the generator's target
   framework, especially `netstandard2.0` compatibility, and use
   `Directory.Packages.props` for package versions. Do not assume a test driver,
   helper, marker attribute, or generator implementation already exists.
3. **Build a narrow incremental pipeline.** Implement `IIncrementalGenerator`, keep
   callbacks static where possible, and avoid mutable instance or global compiler
   state. Filter cheaply, use `ForAttributeWithMetadataName` for an approved marker
   attribute, and project compiler objects into small value-equatable output models
   early. Combine inputs narrowly and collect only when one output truly aggregates
   them. Use declared Roslyn providers for source, additional-file, parse-option, and
   analyzer-config/MSBuild inputs; do not read ambient files or environment state.
4. **Emit deterministic, compatible source.** Produce additive UTF-8 source with a
   generated header, collision-free hint names, correct identifier and literal
   escaping, global qualification, and appropriate nullable and language-version
   behavior. Keep output deterministic and lightweight; use text emission rather
   than building syntax trees only to call `NormalizeWhitespace`.
5. **Test the output and the cache.** Use the NUnit/VSTest setup with
   `Microsoft.CodeAnalysis.Testing` for source/diagnostic assertions and direct
   generator-driver tests where useful for incremental behavior; these approaches
   complement each other. Compile generated sources and check diagnostics. Verify
   caching with tracking names, tracked steps, and the updated driver returned by each
   run. Cover no-op and unrelated edits as well as relevant, added, removed,
   analyzer-config, and additional-file inputs. Source snapshots alone are not
   sufficient.
6. **Validate normally.** Run `dotnet build` and `dotnet test` for the owning projects,
   Debug before Release; use standard repository workflows rather than a new runner.
   If generated code participates in NativeAOT behavior for selected closed roots,
   later acceptance must build and execute the actual published artifact for each
   selected root; declarations or JIT execution alone do not establish AOT behavior.
