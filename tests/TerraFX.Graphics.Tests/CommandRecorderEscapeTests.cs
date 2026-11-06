// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Numerics;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class CommandRecorderEscapeTests
{
    private static void AddAssembly(
        System.Reflection.Assembly assembly,
        ImmutableArray<MetadataReference>.Builder references,
        HashSet<string> seenPaths)
    {
        var path = assembly.Location;

        if ((path.Length != 0) && seenPaths.Add(path))
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }
    }

    private static ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> CompileErrors(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            SourceText.From(source),
            new CSharpParseOptions(LanguageVersion.CSharp13),
            path: "CommandRecorderConsumer.cs");
        var compilation = CSharpCompilation.Create(
            "CommandRecorderConsumer",
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return [.. compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)];
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string)
            ?.Split(Path.PathSeparator)
            ?? throw new InvalidOperationException("The current runtime did not expose its trusted platform assemblies.");
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (seenPaths.Add(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        AddAssembly(typeof(CommandRecorder).Assembly, references, seenPaths);
        AddAssembly(typeof(SpatialProfileId).Assembly, references, seenPaths);
        return references.ToImmutable();
    }

    [Test]
    public void RecorderCannotEscapeToHeapOrCrossAnAwait()
    {
        const string heapEscapeSource = """
            using TerraFX.Graphics;

            internal sealed class RecorderHolder
            {
                internal CommandRecorder Recorder;
            }
            """;
        const string awaitEscapeSource = """
            using System.Threading.Tasks;
            using TerraFX.Graphics;

            internal static class RecorderAwaitConsumer
            {
                internal static async Task Use(scoped ref CommandRecorder recorder)
                {
                    await Task.Yield();
                    recorder.Dispatch(default);
                }
            }
            """;

        var heapEscapeErrors = CompileErrors(heapEscapeSource);
        var awaitEscapeErrors = CompileErrors(awaitEscapeSource);

        Assert.That(heapEscapeErrors.Any(static diagnostic => diagnostic.Id == "CS8345"), Is.True);
        Assert.That(awaitEscapeErrors.Any(static diagnostic => diagnostic.Id == "CS1988"), Is.True);
    }
}
