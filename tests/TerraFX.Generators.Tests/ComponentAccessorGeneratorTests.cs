// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Generators.Tests;

[TestFixture]
internal sealed class ComponentAccessorGeneratorTests
{
    private const string ComponentSourceTemplate = """
        using TerraFX.Generators;

        namespace GeneratorConsumer
        {
            internal static partial class Container
            {
                [GenerateComponentBindings(typeof(Author))]
                private partial struct Component
                {
                    [GenerateComponentFieldAccessor({0}, nameof(Author.Value), "Value")]
                    private int _value;
                }

                private readonly record struct Author(int Value);
        {1}
            }
        }
        """;

    private static void AddAssembly(
        System.Reflection.Assembly assembly,
        ImmutableArray<MetadataReference>.Builder references,
        HashSet<string> seenPaths)
    {
        var path = assembly.Location;

        if (path.Length != 0 && seenPaths.Add(path))
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }
    }

    private static CSharpCompilation CreateCompilation(
        uint firstFieldTag,
        bool includeSecondComponent = false,
        CSharpParseOptions? parseOptions = null)
    {
        var secondComponent = includeSecondComponent
            ? """
                  [GenerateComponentBindings(typeof(SecondAuthor))]
                  private partial struct SecondComponent
                  {
                      [GenerateComponentFieldAccessor(1, nameof(SecondAuthor.Count), "Count")]
                      private int _count;
                  }

                  private readonly record struct SecondAuthor(int Count);
              """
            : string.Empty;
        var source = ComponentSourceTemplate
            .Replace(
                "{0}",
                firstFieldTag.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            .Replace("{1}", secondComponent, StringComparison.Ordinal);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            parseOptions ?? new CSharpParseOptions(LanguageVersion.CSharp13),
            path: "Components.cs");
        return CSharpCompilation.Create(
            "GeneratorConsumer",
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static CSharpGeneratorDriver CreateDriver(CSharpParseOptions? parseOptions = null)
    {
        var generator = new ComponentAccessorGenerator().AsSourceGenerator();
        var options = new GeneratorDriverOptions(
            IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true);
        return CSharpGeneratorDriver.Create(
            [generator],
            parseOptions: parseOptions ?? new CSharpParseOptions(LanguageVersion.CSharp13),
            driverOptions: options);
    }

    private static GeneratedSourceResult[] GetComponentGeneratedSources(GeneratorDriver driver)
        => [.. driver.GetRunResult().Results[0].GeneratedSources
            .Where(static generatedSource => generatedSource.HintName.StartsWith(
                "TerraFX.ComponentBindings.",
                StringComparison.Ordinal))];

    private static ImmutableArray<IncrementalStepRunReason> GetReasons(GeneratorDriver driver)
    {
        var trackedSteps = driver.GetRunResult().Results[0].TrackedSteps;

        if (!trackedSteps.TryGetValue("TerraFX.ComponentAccessorModel", out var steps))
        {
            throw new InvalidOperationException("The component generator's tracked model step was not recorded.");
        }

        var reasons = ImmutableArray.CreateBuilder<IncrementalStepRunReason>();

        foreach (var step in steps)
        {
            foreach (var (_, reason) in step.Outputs)
            {
                reasons.Add(reason);
            }
        }

        return reasons.ToImmutable();
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(System.IO.Path.PathSeparator)
            ?? throw new InvalidOperationException("The current runtime did not expose its trusted platform assemblies.");
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (seenPaths.Add(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        AddAssembly(typeof(ComponentFieldPath).Assembly, references, seenPaths);
        AddAssembly(typeof(CompiledContentSchemaRegistry).Assembly, references, seenPaths);
        return references.ToImmutable();
    }

    private static CSharpGeneratorDriver Run(
        CSharpGeneratorDriver driver,
        CSharpCompilation compilation,
        out CSharpCompilation outputCompilation)
    {
        var updatedDriver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);
        Assert.That(generatorDiagnostics, Is.Empty);
        outputCompilation = (CSharpCompilation)updatedCompilation;
        return (CSharpGeneratorDriver)updatedDriver;
    }

    [Test]
    public void EmitsExplicitFieldAccessorsPathsAndACompilableConsumer()
    {
        var compilation = CreateCompilation(firstFieldTag: 2);
        var driver = Run(CreateDriver(), compilation, out var outputCompilation);
        var errors = outputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();
        var generated = GetComponentGeneratedSources(driver);

        Assert.That(errors, Is.Empty);
        Assert.That(generated, Has.Length.EqualTo(1));
        var source = generated[0].SourceText.ToString();
        Assert.That(source, Does.Contain("Field2Accessor"));
        Assert.That(source, Does.Contain("Field2Reader"));
        Assert.That(source, Does.Contain("Field2FieldPath"));
        Assert.That(source, Does.Contain("FieldTag(2u)"));
        Assert.That(source, Does.Contain("ComponentStorageSegment.Member(\"Value\")"));
    }

    [Test]
    public void IncrementalPipelineCachesUnchangedInputsAndScopesAddedRemovedAndRelevantEdits()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
        var driver = CreateDriver(parseOptions);
        var compilation = CreateCompilation(firstFieldTag: 1, parseOptions: parseOptions);
        driver = Run(driver, compilation, out _);
        var initialGeneratedSource = GetComponentGeneratedSources(driver)[0].SourceText.ToString();

        driver = Run(driver, compilation, out _);
        Assert.That(GetReasons(driver), Does.Contain(IncrementalStepRunReason.Unchanged));
        Assert.That(GetComponentGeneratedSources(driver)[0].SourceText.ToString(), Is.EqualTo(initialGeneratedSource));

        var unrelatedTree = CSharpSyntaxTree.ParseText(
            "namespace GeneratorConsumer { internal sealed class Unrelated { } }",
            parseOptions,
            path: "Unrelated.cs");
        driver = Run(driver, compilation.AddSyntaxTrees(unrelatedTree), out _);
        Assert.That(GetReasons(driver), Does.Contain(IncrementalStepRunReason.Unchanged));

        var changedCompilation = CreateCompilation(firstFieldTag: 3, parseOptions: parseOptions);
        driver = Run(driver, changedCompilation, out _);
        Assert.That(GetReasons(driver), Does.Contain(IncrementalStepRunReason.Modified));

        var addedCompilation = CreateCompilation(firstFieldTag: 3, includeSecondComponent: true, parseOptions);
        driver = Run(driver, addedCompilation, out _);
        Assert.That(GetComponentGeneratedSources(driver), Has.Length.EqualTo(2));
        Assert.That(GetReasons(driver), Does.Contain(IncrementalStepRunReason.New));

        driver = Run(driver, changedCompilation, out _);
        Assert.That(GetComponentGeneratedSources(driver), Has.Length.EqualTo(1));
        Assert.That(GetReasons(driver), Does.Contain(IncrementalStepRunReason.Removed));
    }

    [Test]
    public void InvalidFieldTagProducesAnExplicitCompilerError()
    {
        var compilation = CreateCompilation(firstFieldTag: 0);
        var driver = Run(CreateDriver(), compilation, out var outputCompilation);
        var errors = outputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(GetComponentGeneratedSources(driver), Has.Length.EqualTo(1));
        Assert.That(errors, Has.Some.Matches<Microsoft.CodeAnalysis.Diagnostic>(
            static diagnostic => (diagnostic.Id == "CS1029")
                && diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
                    .Contains("unique positive tag", StringComparison.Ordinal)));
    }

    [Test]
    public void ScopedChunkViewsCannotEscapeToHeapOrAcrossAwait()
    {
        const string source = """
            using System.Threading.Tasks;
            using TerraFX.Entities;

            internal sealed class EscapingViewConsumer
            {
                private EntityChunkView _stored;

                internal static async Task<int> Observe(EntityChunkView chunk)
                {
                    await Task.Yield();
                    return chunk.Entities.Length;
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.CSharp13),
            path: "EscapingViewConsumer.cs");
        var compilation = CSharpCompilation.Create(
            "EscapingViewConsumer",
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .ToArray();

        Assert.That(errors, Has.Some.Matches<Microsoft.CodeAnalysis.Diagnostic>(
            static diagnostic => diagnostic.Id == "CS8345"));
        Assert.That(errors, Has.Some.Matches<Microsoft.CodeAnalysis.Diagnostic>(
            static diagnostic => diagnostic.Id is "CS4007" or "CS4012"));
    }
}
