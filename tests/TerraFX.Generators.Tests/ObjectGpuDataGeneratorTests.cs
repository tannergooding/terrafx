// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using TerraFX.Numerics;

namespace TerraFX.Generators.Tests;

[TestFixture]
internal sealed class ObjectGpuDataGeneratorTests
{
    private const string ConsumerTemplate = """
        using TerraFX.Generators;
        using TerraFX.Numerics;

        namespace TerraFX.Rendering
        {
            [GenerateObjectGpuDataPacker]
            internal readonly partial struct ObjectGpuData
            {
                private readonly Matrix4x4 _world;
                private readonly Matrix4x4 _previousWorld;
                private readonly Vector4 _normalRow0;
                private readonly Vector4 _normalRow1;
                private readonly Vector4 _normalRow2;
                private readonly uint _metadata0;
                private readonly uint _metadata1;
                private readonly uint _metadata2;
                private readonly uint _metadata3;
            }
        {0}
        }
        """;

    private static CSharpCompilation CreateCompilation(bool includeSecondTarget = false, bool validFields = true)
    {
        var secondTarget = includeSecondTarget
            ? """
                  namespace TerraFX.Rendering.Second
                  {
                      [GenerateObjectGpuDataPacker]
                      internal readonly partial struct ObjectGpuData
                      {
                          private readonly Matrix4x4 _world;
                          private readonly Matrix4x4 _previousWorld;
                          private readonly Vector4 _normalRow0;
                          private readonly Vector4 _normalRow1;
                          private readonly Vector4 _normalRow2;
                          private readonly uint _metadata0;
                          private readonly uint _metadata1;
                          private readonly uint _metadata2;
                          private readonly uint _metadata3;
                      }
                  }
              """
            : string.Empty;
        var source = ConsumerTemplate.Replace("{0}", secondTarget, StringComparison.Ordinal);

        if (!validFields)
        {
            source = source.Replace("private readonly uint _metadata0;", "private readonly float _metadata0;", StringComparison.Ordinal);
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp13);
        var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(source), parseOptions, path: "ObjectGpuData.cs");
        return CSharpCompilation.Create(
            "ObjectGpuDataConsumer",
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static CSharpGeneratorDriver CreateDriver()
        => CSharpGeneratorDriver.Create(
            [new ObjectGpuDataGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.CSharp13),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static GeneratedSourceResult[] GetGeneratedSources(GeneratorDriver driver)
        => [.. driver.GetRunResult().Results[0].GeneratedSources
            .Where(static result => result.HintName.StartsWith("TerraFX.ObjectGpuDataPacker.", StringComparison.Ordinal))];

    private static ImmutableArray<IncrementalStepRunReason> GetReasons(GeneratorDriver driver, string trackingName)
    {
        var trackedSteps = driver.GetRunResult().Results[0].TrackedSteps;

        if (!trackedSteps.TryGetValue(trackingName, out var steps))
        {
            throw new InvalidOperationException(
                $"The tracked generator step '{trackingName}' was not recorded. Available: {string.Join(", ", trackedSteps.Keys)}");
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
        var trustedAssemblyPaths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(System.IO.Path.PathSeparator)
            ?? throw new InvalidOperationException("The current runtime did not expose its trusted platform assemblies.");
        var references = ImmutableArray.CreateBuilder<MetadataReference>();

        foreach (var path in trustedAssemblyPaths)
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        references.Add(MetadataReference.CreateFromFile(typeof(Matrix4x4).Assembly.Location));
        return references.ToImmutable();
    }

    private static CSharpGeneratorDriver Run(CSharpGeneratorDriver driver, CSharpCompilation compilation, out CSharpCompilation outputCompilation)
    {
        var updatedDriver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var generatorDiagnostics);
        Assert.That(generatorDiagnostics, Is.Empty);
        outputCompilation = (CSharpCompilation)updatedCompilation;
        return (CSharpGeneratorDriver)updatedDriver;
    }

    [Test]
    public void EmitsFixedOffsetsAndCompilesTheRealConsumer()
    {
        var driver = Run(CreateDriver(), CreateCompilation(), out var outputCompilation);
        var generated = GetGeneratedSources(driver);

        Assert.That(outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), Is.Empty);
        Assert.That(generated, Has.Length.EqualTo(1));
        var source = generated[0].SourceText.ToString();
        Assert.That(source, Does.Contain("SizeInBytes = 192"));
        Assert.That(source, Does.Contain("WriteSingle(destination, 0, _world.Row1.X)"));
        Assert.That(source, Does.Contain("WriteSingle(destination, 64, _previousWorld.Row1.X)"));
        Assert.That(source, Does.Contain("WriteSingle(destination, 128, _normalRow0.X)"));
        Assert.That(source, Does.Contain("WriteUInt32(destination, 176, _metadata0)"));
        Assert.That(source, Does.Contain("WriteUInt32(destination, 188, _metadata3)"));
        Assert.That(source, Does.Contain("GetUniformAllocationStride"));
    }

    [Test]
    public void IncrementalPipelineRetainsNoOpWorkAndTracksCandidateChanges()
    {
        var driver = CreateDriver();
        var compilation = CreateCompilation();
        driver = Run(driver, compilation, out _);
        driver = Run(driver, compilation, out _);
        Assert.That(GetReasons(driver, "TerraFX.ObjectGpuDataPackerModel"), Does.Contain(IncrementalStepRunReason.Unchanged));

        var unrelatedTree = CSharpSyntaxTree.ParseText(
            "namespace Unrelated { internal sealed class Type { } }",
            new CSharpParseOptions(LanguageVersion.CSharp13),
            path: "Unrelated.cs");
        driver = Run(driver, compilation.AddSyntaxTrees(unrelatedTree), out _);
        Assert.That(GetReasons(driver, "TerraFX.ObjectGpuDataPackerModel"), Does.Contain(IncrementalStepRunReason.Unchanged));

        var addedCompilation = CreateCompilation(includeSecondTarget: true);
        driver = Run(driver, addedCompilation, out var addedOutput);
        Assert.That(GetGeneratedSources(driver), Has.Length.EqualTo(2));
        Assert.That(GetReasons(driver, "TerraFX.ObjectGpuDataPackerModel"), Does.Contain(IncrementalStepRunReason.New));
        Assert.That(addedOutput.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error), Is.Empty);

        driver = Run(driver, CreateCompilation(), out _);
        Assert.That(GetGeneratedSources(driver), Has.Length.EqualTo(1));
        Assert.That(GetReasons(driver, "TerraFX.ObjectGpuDataPackerModel"), Does.Contain(IncrementalStepRunReason.Removed));

        driver = Run(driver, CreateCompilation(validFields: false), out var invalidOutput);
        Assert.That(GetReasons(driver, "TerraFX.ObjectGpuDataPackerModel"), Does.Contain(IncrementalStepRunReason.Modified));
        Assert.That(invalidOutput.GetDiagnostics(), Has.Some.Matches<Microsoft.CodeAnalysis.Diagnostic>(
            static diagnostic => diagnostic.Id == "CS1029"));
    }

    [Test]
    public void RejectsInvalidFixedFieldContractWithCompilerError()
    {
        var driver = Run(CreateDriver(), CreateCompilation(validFields: false), out var outputCompilation);
        var generated = GetGeneratedSources(driver);
        var errors = outputCompilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();

        Assert.That(generated, Has.Length.EqualTo(1));
        Assert.That(errors, Has.Some.Matches<Microsoft.CodeAnalysis.Diagnostic>(
            static diagnostic => (diagnostic.Id == "CS1029")
                && diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).Contains("fixed field contract", StringComparison.Ordinal)));
    }
}
