// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using TerraFX.Graphics;

namespace TerraFX.Content.Tools;

internal static class M04ShaderReflectionReader
{
    private const uint CapabilityMatrix = 0;
    private const uint CapabilityShader = 1;
    private const uint MagicNumber = 0x07230203;
    private const ushort OpCapability = 17;

    internal static ShaderReflection Read(
        string dxilReflection,
        string spirvReflection,
        ReadOnlySpan<byte> spirvBytecode,
        ShaderCompilerIdentity compilerIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dxilReflection);
        ArgumentException.ThrowIfNullOrWhiteSpace(spirvReflection);

        if (!compilerIdentity.IsValid)
        {
            throw new ArgumentException("Typed shader reflection requires the pinned compiler identity.", nameof(compilerIdentity));
        }

        using var document = JsonDocument.Parse(spirvReflection, new JsonDocumentOptions {
            MaxDepth = 32,
        });
        var root = document.RootElement;
        var entryPoints = root.GetProperty("entryPoints");

        if ((entryPoints.ValueKind != JsonValueKind.Array) || (entryPoints.GetArrayLength() != 1))
        {
            throw new InvalidDataException("Typed M04 reflection requires exactly one SPIRV-Cross entry point.");
        }

        var entryPoint = entryPoints[0];
        var entryPointName = GetRequiredString(entryPoint, "name");
        var stage = GetStage(GetRequiredString(entryPoint, "mode"));

        if (!dxilReflection.Contains($"EntryFunctionName: {entryPointName}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("DXIL and SPIRV-Cross reflection name different entry points.");
        }

        var imageTypes = SpirvImageTypeReader.Read(spirvBytecode);
        var resources = ParseResources(root, imageTypes);
        ValidateDxilResourceBindings(dxilReflection, resources);
        var inputs = ParseDxilSignatures(dxilReflection, "Input signature:", stage, isInput: true);
        var outputs = ParseDxilSignatures(dxilReflection, "Output signature:", stage, isInput: false);
        ValidateSpirvCrossIo(root, "inputs", inputs, stage, isInput: true);
        ValidateSpirvCrossIo(root, "outputs", outputs, stage, isInput: false);
        var recordLayouts = ParseRecordLayouts(root);
        DxilRecordLayoutReader.Validate(dxilReflection, recordLayouts);
        var requiredCapabilities = ParseCapabilities(spirvBytecode);

        if (stage == ShaderStage.Compute)
        {
            throw new NotSupportedException("The selected M04 reflection reader only converts graphics-stage fixtures.");
        }

        var signatures = ImmutableArray.CreateBuilder<ShaderResourceSignature>(resources.Length);

        foreach (var resource in resources)
        {
            signatures.Add(resource.Signature);
        }

        return new ShaderReflection(
            stage,
            entryPointName,
            compilerIdentity,
            inputs,
            outputs,
            signatures.MoveToImmutable(),
            recordLayouts,
            requiredCapabilities,
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);
    }

    private static void AddResourceCollection(
        JsonElement root,
        string collectionName,
        ShaderResourceKind kind,
        Dictionary<(int DescriptorSet, int Binding), (string ComponentFormat, bool IsStorage, string Dimension)> imageTypes,
        ImmutableArray<ReflectedResource>.Builder resources)
    {
        if (!root.TryGetProperty(collectionName, out var collection))
        {
            return;
        }

        if (collection.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidDataException($"SPIRV-Cross resource collection '{collectionName}' must be an array.");
        }

        foreach (var resource in collection.EnumerateArray())
        {
            var name = NormalizeResourceName(GetRequiredString(resource, "name"));
            var spirvType = GetRequiredString(resource, "type");
            var descriptorSet = GetRequiredInt32(resource, "set");
            var nativeBinding = GetRequiredInt32(resource, "binding");
            var group = GetGroup(descriptorSet);
            var binding = checked(nativeBinding - GetBindingOffset(kind));
            string? format = null;
            string? componentFormat = null;

            if (kind is ShaderResourceKind.SampledImage or ShaderResourceKind.StorageImage)
            {
                if (!imageTypes.TryGetValue((descriptorSet, nativeBinding), out var imageType))
                {
                    throw new InvalidDataException($"SPIR-V has no image type for descriptor set {descriptorSet}, binding {nativeBinding}.");
                }

                if (imageType.IsStorage != (kind == ShaderResourceKind.StorageImage))
                {
                    throw new InvalidDataException("SPIR-V image sampling mode does not match the SPIRV-Cross resource collection.");
                }

                if (!StringComparer.Ordinal.Equals(
                    imageType.Dimension,
                    SpirvImageTypeReader.GetCrossDimension(spirvType, kind == ShaderResourceKind.StorageImage)))
                {
                    throw new InvalidDataException("SPIRV-Cross image dimension does not match the reflected SPIR-V image type.");
                }

                componentFormat = imageType.ComponentFormat;
            }

            if (resource.TryGetProperty("format", out var formatElement))
            {
                if ((formatElement.ValueKind != JsonValueKind.String)
                    || string.IsNullOrWhiteSpace(formatElement.GetString()))
                {
                    throw new InvalidDataException("SPIRV-Cross resource formats must be nonempty strings when present.");
                }

                format = formatElement.GetString();
            }

            if (binding < 0)
            {
                throw new InvalidDataException($"SPIRV-Cross binding {nativeBinding} is invalid for resource class {kind}.");
            }

            if (kind == ShaderResourceKind.StorageImage)
            {
                if (!spirvType.Contains("image", StringComparison.OrdinalIgnoreCase)
                    || spirvType.Contains("sampler", StringComparison.OrdinalIgnoreCase))
                {
                    throw new NotSupportedException("The selected M04 reflection reader does not convert combined image-sampler resources.");
                }

                if (format is null)
                {
                    throw new InvalidDataException("SPIRV-Cross storage-image reflection must include its format.");
                }
            }

            resources.Add(new ReflectedResource(
                name,
                new ShaderResourceSignature(group, binding, kind, GetArrayCount(resource)),
                spirvType,
                format,
                componentFormat));
        }
    }

    private static void CheckUnsupportedResourceCollections(JsonElement root)
    {
        foreach (var collectionName in new[] { "textures", "subpass_inputs", "acceleration_structures" })
        {
            if (!root.TryGetProperty(collectionName, out var collection))
            {
                continue;
            }

            if (collection.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"SPIRV-Cross resource collection '{collectionName}' must be an array.");
            }

            if (collection.GetArrayLength() != 0)
            {
                throw new NotSupportedException($"The selected M04 reflection reader does not convert '{collectionName}' resources.");
            }
        }
    }

    private static int GetArrayCount(JsonElement element)
    {
        if (!element.TryGetProperty("array", out var array))
        {
            return 1;
        }

        if ((array.ValueKind != JsonValueKind.Array) || (array.GetArrayLength() == 0))
        {
            throw new InvalidDataException("SPIRV-Cross array reflection must contain at least one dimension.");
        }

        if (element.TryGetProperty("array_size_is_literal", out var literalSizes))
        {
            if ((literalSizes.ValueKind != JsonValueKind.Array)
                || (literalSizes.GetArrayLength() != array.GetArrayLength()))
            {
                throw new InvalidDataException("SPIRV-Cross array literal markers must match the reflected dimensions.");
            }

            foreach (var literalSize in literalSizes.EnumerateArray())
            {
                if (literalSize.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new InvalidDataException("SPIRV-Cross array literal markers must be Boolean.");
                }

                if (!literalSize.GetBoolean())
                {
                    throw new NotSupportedException("Runtime-sized or specialization-sized shader arrays are not supported.");
                }
            }
        }

        var count = 1;

        foreach (var dimension in array.EnumerateArray())
        {
            if ((dimension.ValueKind != JsonValueKind.Number) || !dimension.TryGetInt32(out var dimensionCount) || (dimensionCount <= 0))
            {
                throw new NotSupportedException("Shader arrays must have positive fixed dimensions.");
            }

            count = checked(count * dimensionCount);
        }

        return count;
    }

    private static int GetBindingOffset(ShaderResourceKind kind)
        => kind switch {
            ShaderResourceKind.UniformBuffer => 0,
            ShaderResourceKind.SampledImage => 16,
            ShaderResourceKind.StorageBuffer or ShaderResourceKind.StorageImage => 32,
            ShaderResourceKind.Sampler => 40,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static int? GetExpectedLocation(ShaderIoSignature signature, ShaderStage stage, bool isInput)
    {
        if (signature.Location is int location)
        {
            return location;
        }

        if (!isInput
            && (stage == ShaderStage.Pixel)
            && StringComparer.OrdinalIgnoreCase.Equals(signature.SemanticName, "SV_Target"))
        {
            return signature.SemanticIndex;
        }

        return null;
    }

    private static ShaderBindingGroup GetGroup(int descriptorSet)
        => descriptorSet switch {
            0 => ShaderBindingGroup.Frame,
            1 => ShaderBindingGroup.Material,
            2 => ShaderBindingGroup.ObjectData,
            3 => ShaderBindingGroup.Pass,
            _ => throw new NotSupportedException($"SPIRV-Cross descriptor set {descriptorSet} is outside the M04 binding groups."),
        };

    private static ShaderInterpolation GetInterpolation(
        List<string[]> interpolationRows,
        string semanticName,
        int semanticIndex,
        bool isInput)
    {
        foreach (var row in interpolationRows)
        {
            if ((row.Length < 2)
                || !StringComparer.Ordinal.Equals(row[0], semanticName)
                || !int.TryParse(row[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rowIndex)
                || (rowIndex != semanticIndex))
            {
                continue;
            }

            if (row.Length < 3)
            {
                break;
            }

            if (row.Length > 3)
            {
                throw new NotSupportedException("Dynamic or sample-qualified DXIL stage I/O is not supported by the M04 reflection reader.");
            }

            return row[2] switch {
                "linear" => ShaderInterpolation.Smooth,
                "noperspective" => ShaderInterpolation.NoPerspective,
                "nointerpolation" => ShaderInterpolation.Flat,
                _ => throw new NotSupportedException($"DXIL interpolation mode '{row[2]}' is not supported."),
            };
        }

        if (isInput
            && (StringComparer.OrdinalIgnoreCase.Equals(semanticName, "SV_VertexID")
                || StringComparer.OrdinalIgnoreCase.Equals(semanticName, "SV_InstanceID")))
        {
            return ShaderInterpolation.Flat;
        }

        return ShaderInterpolation.Smooth;
    }

    private static int GetRequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || (value.ValueKind != JsonValueKind.Number)
            || !value.TryGetInt32(out var result))
        {
            throw new InvalidDataException($"SPIRV-Cross property '{propertyName}' must be a 32-bit integer.");
        }

        return result;
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || (value.ValueKind != JsonValueKind.String)
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"SPIRV-Cross property '{propertyName}' must be a nonempty string.");
        }

        return value.GetString()!;
    }

    private static ReadOnlySpan<uint> GetSpirvWords(ReadOnlySpan<byte> bytecode)
    {
        if ((bytecode.Length < (5 * sizeof(uint))) || ((bytecode.Length % sizeof(uint)) != 0))
        {
            throw new InvalidDataException("The reflected SPIR-V module has an invalid byte length.");
        }

        var words = MemoryMarshal.Cast<byte, uint>(bytecode);

        if (words[0] != MagicNumber)
        {
            throw new InvalidDataException("The reflected shader bytecode is not SPIR-V.");
        }

        return words;
    }

    private static ShaderStage GetStage(string mode)
        => mode switch {
            "vert" => ShaderStage.Vertex,
            "frag" => ShaderStage.Pixel,
            _ => throw new NotSupportedException($"SPIRV-Cross stage '{mode}' is not supported by the M04 reflection reader."),
        };

    private static int GetTypeByteLength(ShaderValueType valueType)
        => valueType switch {
            ShaderValueType.Float1 or ShaderValueType.Int1 or ShaderValueType.UInt1 => sizeof(uint),
            ShaderValueType.Float2 or ShaderValueType.Int2 or ShaderValueType.UInt2 => 2 * sizeof(uint),
            ShaderValueType.Float3 or ShaderValueType.Int3 or ShaderValueType.UInt3 => 3 * sizeof(uint),
            ShaderValueType.Float4 or ShaderValueType.Int4 or ShaderValueType.UInt4 => 4 * sizeof(uint),
            ShaderValueType.Float4x4 => 16 * sizeof(uint),
            _ => throw new ArgumentOutOfRangeException(nameof(valueType)),
        };

    private static string NormalizeResourceName(string name)
        => name.StartsWith("type.", StringComparison.Ordinal) ? name[5..] : name;

    private static ImmutableArray<ShaderCapability> ParseCapabilities(ReadOnlySpan<byte> bytecode)
    {
        var words = GetSpirvWords(bytecode);

        for (var cursor = 5; cursor < words.Length;)
        {
            var instruction = words[cursor];
            var wordCount = checked((int)(instruction >> 16));
            var opcode = checked((ushort)(instruction & 0xFFFF));

            if ((wordCount == 0) || (wordCount > words.Length - cursor))
            {
                throw new InvalidDataException("The reflected SPIR-V module contains a truncated instruction.");
            }

            if (opcode == OpCapability)
            {
                if (wordCount != 2)
                {
                    throw new InvalidDataException("SPIR-V OpCapability has an invalid operand count.");
                }

                if (words[cursor + 1] is not (CapabilityMatrix or CapabilityShader))
                {
                    throw new NotSupportedException("The M04 reflection reader only converts Shader/Matrix baseline capabilities.");
                }
            }

            cursor += wordCount;
        }

        return [];
    }

    private static (string RegisterClass, int Binding) ParseDxilRegister(string register)
    {
        var registerClass = register.StartsWith("cb", StringComparison.Ordinal)
            ? "cb"
            : register[..1];
        var registerIndex = register.AsSpan(registerClass.Length);

        if (!int.TryParse(registerIndex, NumberStyles.None, CultureInfo.InvariantCulture, out var binding))
        {
            throw new InvalidDataException($"DXIL register '{register}' is not a supported local binding.");
        }

        return (registerClass, binding);
    }

    private static ImmutableArray<ShaderIoSignature> ParseDxilSignatures(
        string dxilReflection,
        string heading,
        ShaderStage stage,
        bool isInput)
    {
        var signatureRows = ParseTableRows(dxilReflection, heading, occurrence: 0, required: true);
        var interpolationRows = ParseTableRows(dxilReflection, heading, occurrence: 1, required: false);
        var signatures = ImmutableArray.CreateBuilder<ShaderIoSignature>(signatureRows.Count);

        foreach (var row in signatureRows)
        {
            if (row.Length < 6
                || !int.TryParse(row[1], NumberStyles.None, CultureInfo.InvariantCulture, out var semanticIndex)
                || !int.TryParse(row[3], NumberStyles.None, CultureInfo.InvariantCulture, out var register))
            {
                throw new InvalidDataException("A DXIL signature row has an invalid index or register.");
            }

            if (isInput && ((row.Length < 7) || (row[6].Length == 0)))
            {
                continue;
            }

            var semanticName = row[0];
            var valueType = ParseDxilValueType(row[5], row[2]);
            int? location = semanticName.StartsWith("SV_", StringComparison.OrdinalIgnoreCase) ? null : register;
            var interpolation = GetInterpolation(interpolationRows, semanticName, semanticIndex, isInput);
            signatures.Add(new ShaderIoSignature(semanticName, semanticIndex, location, valueType, interpolation));
        }

        return signatures.ToImmutable();
    }

    private static ShaderValueType ParseDxilValueType(string format, string mask)
    {
        var componentCount = mask switch {
            "x" => 1,
            "xy" => 2,
            "xyz" => 3,
            "xyzw" => 4,
            _ => throw new NotSupportedException($"DXIL component mask '{mask}' is not supported by the M04 reflection reader."),
        };

        return (format, componentCount) switch {
            ("float", 1) => ShaderValueType.Float1,
            ("float", 2) => ShaderValueType.Float2,
            ("float", 3) => ShaderValueType.Float3,
            ("float", 4) => ShaderValueType.Float4,
            ("int", 1) => ShaderValueType.Int1,
            ("int", 2) => ShaderValueType.Int2,
            ("int", 3) => ShaderValueType.Int3,
            ("int", 4) => ShaderValueType.Int4,
            ("uint", 1) => ShaderValueType.UInt1,
            ("uint", 2) => ShaderValueType.UInt2,
            ("uint", 3) => ShaderValueType.UInt3,
            ("uint", 4) => ShaderValueType.UInt4,
            _ => throw new NotSupportedException($"DXIL value format '{format}' with mask '{mask}' is not supported by the M04 reflection reader."),
        };
    }

    private static ImmutableArray<ShaderRecordLayout> ParseRecordLayouts(JsonElement root)
    {
        if (!root.TryGetProperty("ubos", out var uniformBuffers))
        {
            return [];
        }

        if ((uniformBuffers.ValueKind != JsonValueKind.Array) || (uniformBuffers.GetArrayLength() == 0))
        {
            throw new InvalidDataException("SPIRV-Cross UBO reflection must be an initialized nonempty array when present.");
        }

        var types = root.GetProperty("types");
        var layouts = ImmutableArray.CreateBuilder<ShaderRecordLayout>(uniformBuffers.GetArrayLength());

        foreach (var uniformBuffer in uniformBuffers.EnumerateArray())
        {
            var typeIdentifier = GetRequiredString(uniformBuffer, "type");
            var type = types.GetProperty(typeIdentifier);
            var members = type.GetProperty("members");

            if ((members.ValueKind != JsonValueKind.Array) || (members.GetArrayLength() == 0))
            {
                throw new NotSupportedException("The M04 reflection reader requires nonempty flat UBO records.");
            }

            var fields = ImmutableArray.CreateBuilder<ShaderFieldLayout>(members.GetArrayLength());

            foreach (var member in members.EnumerateArray())
            {
                fields.Add(DxilRecordLayoutReader.ParseSpirvRecordField(member));
            }

            var typeName = type.TryGetProperty("name", out _)
                ? GetRequiredString(type, "name")
                : GetRequiredString(uniformBuffer, "name");

            try
            {
                layouts.Add(new ShaderRecordLayout(
                    NormalizeResourceName(typeName),
                    GetRequiredInt32(uniformBuffer, "block_size"),
                    fields.MoveToImmutable()));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("SPIRV-Cross reported an invalid uniform-buffer record layout.", exception);
            }
        }

        return layouts.MoveToImmutable();
    }

    private static ImmutableArray<ReflectedResource> ParseResources(
        JsonElement root,
        Dictionary<(int DescriptorSet, int Binding), (string ComponentFormat, bool IsStorage, string Dimension)> imageTypes)
    {
        CheckUnsupportedResourceCollections(root);
        var resources = ImmutableArray.CreateBuilder<ReflectedResource>();
        AddResourceCollection(root, "ubos", ShaderResourceKind.UniformBuffer, imageTypes, resources);
        AddResourceCollection(root, "separate_images", ShaderResourceKind.SampledImage, imageTypes, resources);
        AddResourceCollection(root, "separate_samplers", ShaderResourceKind.Sampler, imageTypes, resources);
        AddResourceCollection(root, "ssbos", ShaderResourceKind.StorageBuffer, imageTypes, resources);
        AddResourceCollection(root, "images", ShaderResourceKind.StorageImage, imageTypes, resources);
        return resources.ToImmutable();
    }

    private static ShaderValueType ParseSpirvType(string type)
        => type switch {
            "float" => ShaderValueType.Float1,
            "vec2" => ShaderValueType.Float2,
            "vec3" => ShaderValueType.Float3,
            "vec4" => ShaderValueType.Float4,
            "mat4" => ShaderValueType.Float4x4,
            "int" => ShaderValueType.Int1,
            "ivec2" => ShaderValueType.Int2,
            "ivec3" => ShaderValueType.Int3,
            "ivec4" => ShaderValueType.Int4,
            "uint" => ShaderValueType.UInt1,
            "uvec2" => ShaderValueType.UInt2,
            "uvec3" => ShaderValueType.UInt3,
            "uvec4" => ShaderValueType.UInt4,
            _ => throw new NotSupportedException($"SPIRV-Cross value type '{type}' is not supported by the M04 reflection reader."),
        };

    private static List<string[]> ParseTableRows(string text, string heading, int occurrence, bool required)
    {
        var lines = text.Split('\n');
        var sectionIndex = -1;
        var sectionOccurrence = 0;
        var marker = $"; {heading}";

        for (var index = 0; index < lines.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(lines[index].Trim(), marker))
            {
                continue;
            }

            if (sectionOccurrence++ == occurrence)
            {
                sectionIndex = index;
                break;
            }
        }

        if (sectionIndex < 0)
        {
            if (required)
            {
                throw new InvalidDataException($"DXIL reflection omitted the '{heading}' table.");
            }

            return [];
        }

        var rows = new List<string[]>();
        var headerFound = false;

        for (var index = sectionIndex + 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (!line.StartsWith(';', StringComparison.Ordinal))
            {
                if (rows.Count != 0)
                {
                    break;
                }

                continue;
            }

            var content = line[1..].Trim();

            if (content.Length == 0)
            {
                if (rows.Count != 0)
                {
                    break;
                }

                continue;
            }

            if (content.EndsWith(':', StringComparison.Ordinal))
            {
                break;
            }

            if (content.StartsWith("Name ", StringComparison.Ordinal))
            {
                headerFound = true;
                continue;
            }

            if (content.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            if (headerFound)
            {
                rows.Add(content.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        return rows;
    }

    private static ShaderStage ReadStage(JsonElement root, out string entryPointName)
    {
        var entryPoints = root.GetProperty("entryPoints");

        if ((entryPoints.ValueKind != JsonValueKind.Array) || (entryPoints.GetArrayLength() != 1))
        {
            throw new InvalidDataException("Typed M04 reflection requires exactly one SPIRV-Cross entry point.");
        }

        var entryPoint = entryPoints[0];
        entryPointName = GetRequiredString(entryPoint, "name");
        return GetRequiredString(entryPoint, "mode") switch {
            "vert" => ShaderStage.Vertex,
            "frag" => ShaderStage.Pixel,
            _ => throw new NotSupportedException("The M04 shader fixture reader supports vertex and pixel stages only."),
        };
    }

    private static void ValidateDxilResourceBindings(string text, ImmutableArray<ReflectedResource> resources)
    {
        static string GetDxilStorageImageFormat(string? format)
        {
            return format switch {
                "r32f" => "f32",
                "r32i" => "i32",
                "r32ui" => "u32",
                _ => throw new NotSupportedException($"SPIRV-Cross storage-image format '{format}' has no supported DXIL format mapping."),
            };
        }

        static (string Type, string? Format, string Dimension) GetDxilResourceShape(ReflectedResource resource)
        {
            return resource.Signature.Kind switch {
                ShaderResourceKind.UniformBuffer => ("cbuffer", "NA", "NA"),
                ShaderResourceKind.SampledImage => (
                    "texture",
                    resource.ComponentFormat,
                    SpirvImageTypeReader.GetCrossDimension(resource.SpirvType, isStorage: false)),
                ShaderResourceKind.StorageBuffer => ("UAV", "struct", "r/w"),
                ShaderResourceKind.StorageImage => (
                    "UAV",
                    GetDxilStorageImageFormat(resource.Format),
                    SpirvImageTypeReader.GetCrossDimension(resource.SpirvType, isStorage: true)),
                ShaderResourceKind.Sampler => ("sampler", "NA", "NA"),
                _ => throw new ArgumentOutOfRangeException(nameof(resource)),
            };
        }

        var rows = ParseTableRows(text, "Resource Bindings:", occurrence: 0, required: true);

        if (rows.Count != resources.Length)
        {
            throw new InvalidDataException("DXIL and SPIRV-Cross reflection report different resource counts.");
        }

        var matched = new bool[resources.Length];

        foreach (var row in rows)
        {
            if ((row.Length != 7)
                || !int.TryParse(row[6], NumberStyles.None, CultureInfo.InvariantCulture, out var arrayCount))
            {
                throw new InvalidDataException("A DXIL resource-binding row has an invalid column count or descriptor count.");
            }

            var (registerClass, binding) = ParseDxilRegister(row[5]);
            var resourceName = NormalizeResourceName(row[0]);
            var matchedIndex = -1;

            for (var index = 0; index < resources.Length; index++)
            {
                if (matched[index])
                {
                    continue;
                }

                var resource = resources[index];
                var expectedRegisterClass = resource.Signature.Kind switch {
                    ShaderResourceKind.UniformBuffer => "cb",
                    ShaderResourceKind.SampledImage => "t",
                    ShaderResourceKind.StorageImage or ShaderResourceKind.StorageBuffer => "u",
                    ShaderResourceKind.Sampler => "s",
                    _ => throw new ArgumentOutOfRangeException(nameof(resources)),
                };

                if (StringComparer.Ordinal.Equals(resource.Name, resourceName)
                    && StringComparer.Ordinal.Equals(expectedRegisterClass, registerClass)
                    && (resource.Signature.Binding == binding)
                    && (resource.Signature.ArrayCount == arrayCount))
                {
                    matchedIndex = index;
                    break;
                }
            }

            if (matchedIndex < 0)
            {
                throw new InvalidDataException($"DXIL resource '{resourceName}' does not match the typed SPIRV-Cross bindings.");
            }

            var matchedResource = resources[matchedIndex];
            var expectedShape = GetDxilResourceShape(matchedResource);

            if ((matchedResource.Signature.Kind == ShaderResourceKind.StorageImage)
                && !StringComparer.Ordinal.Equals(matchedResource.ComponentFormat, expectedShape.Format))
            {
                throw new InvalidDataException($"DXIL storage-image resource '{resourceName}' does not match the SPIR-V component type.");
            }

            if (!StringComparer.Ordinal.Equals(row[1], expectedShape.Type)
                || ((expectedShape.Format is not null) && !StringComparer.Ordinal.Equals(row[2], expectedShape.Format))
                || !StringComparer.Ordinal.Equals(row[3], expectedShape.Dimension))
            {
                throw new InvalidDataException($"DXIL resource '{resourceName}' does not match the SPIRV-Cross resource type, format, or dimension.");
            }

            matched[matchedIndex] = true;
        }
    }

    private static void ValidateSpirvCrossIo(
        JsonElement root,
        string collectionName,
        ImmutableArray<ShaderIoSignature> signatures,
        ShaderStage stage,
        bool isInput)
    {
        var expectedLocations = new bool[signatures.Length];
        var expectedLocationCount = 0;

        for (var index = 0; index < signatures.Length; index++)
        {
            if (GetExpectedLocation(signatures[index], stage, isInput) is not null)
            {
                expectedLocationCount++;
            }
        }

        if (!root.TryGetProperty(collectionName, out var values))
        {
            if (expectedLocationCount != 0)
            {
                throw new InvalidDataException($"SPIRV-Cross omitted the '{collectionName}' stage-I/O collection.");
            }

            return;
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"SPIRV-Cross stage-I/O collection '{collectionName}' must be an array.");
        }

        foreach (var value in values.EnumerateArray())
        {
            if (!value.TryGetProperty("location", out var locationElement)
                || (locationElement.ValueKind != JsonValueKind.Number)
                || !locationElement.TryGetInt32(out var location))
            {
                throw new NotSupportedException("The M04 typed reflection reader does not convert SPIRV-Cross stage I/O without a Location.");
            }

            var valueType = ParseSpirvType(GetRequiredString(value, "type"));
            var matchedIndex = -1;

            for (var index = 0; index < signatures.Length; index++)
            {
                var signature = signatures[index];

                if (GetExpectedLocation(signature, stage, isInput) == location)
                {
                    if (signature.ValueType != valueType)
                    {
                        throw new InvalidDataException($"SPIRV-Cross {collectionName} value types do not match the typed DXIL stage-I/O signatures.");
                    }

                    if (expectedLocations[index])
                    {
                        throw new InvalidDataException($"SPIRV-Cross {collectionName} contains a duplicate stage-I/O location.");
                    }

                    matchedIndex = index;
                    break;
                }
            }

            if (matchedIndex < 0)
            {
                throw new InvalidDataException($"SPIRV-Cross {collectionName} do not match the typed DXIL stage-I/O signatures.");
            }

            expectedLocations[matchedIndex] = true;
        }

        for (var index = 0; index < signatures.Length; index++)
        {
            if ((GetExpectedLocation(signatures[index], stage, isInput) is not null)
                && !expectedLocations[index])
            {
                throw new InvalidDataException($"SPIRV-Cross {collectionName} omitted a DXIL stage-I/O location.");
            }
        }
    }

    private static class DxilRecordLayoutReader
    {
        internal static ShaderFieldLayout ParseSpirvRecordField(JsonElement member)
        {
            var valueType = ParseSpirvType(GetRequiredString(member, "type"));
            var hasArray = member.TryGetProperty("array", out var array);

            if (hasArray && ((array.ValueKind != JsonValueKind.Array) || (array.GetArrayLength() != 1)))
            {
                throw new NotSupportedException("The M04 reflection reader only converts one-dimensional record arrays.");
            }

            var arrayCount = GetArrayCount(member);

            if (hasArray && (arrayCount == 1))
            {
                throw new NotSupportedException("The reflected record layout cannot distinguish a one-element array from a scalar field.");
            }

            var byteLength = GetTypeByteLength(valueType);
            var arrayStride = (arrayCount == 1) ? 0 : GetRecordArrayStride(byteLength);

            if ((arrayCount > 1) && (GetRequiredInt32(member, "array_stride") != arrayStride))
            {
                throw new InvalidDataException("SPIRV-Cross record-array stride does not match the supported HLSL layout.");
            }

            if (valueType == ShaderValueType.Float4x4)
            {
                if ((GetRequiredInt32(member, "matrix_stride") != 16)
                    || !member.TryGetProperty("row_major", out var rowMajor)
                    || (rowMajor.ValueKind != JsonValueKind.True))
                {
                    throw new InvalidDataException("The M04 record layout requires 16-byte row-major float4x4 matrices.");
                }
            }
            else if (member.TryGetProperty("matrix_stride", out _))
            {
                throw new InvalidDataException("SPIRV-Cross reported matrix layout metadata for a non-matrix record field.");
            }

            try
            {
                return new ShaderFieldLayout(
                    GetRequiredString(member, "name"),
                    valueType,
                    GetRequiredInt32(member, "offset"),
                    byteLength,
                    arrayCount,
                    arrayStride);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("SPIRV-Cross reported an invalid uniform-buffer field layout.", exception);
            }
        }

        internal static void Validate(string dxilReflection, ImmutableArray<ShaderRecordLayout> spirvLayouts)
        {
            var dxilLayouts = ParseDxilRecordLayouts(dxilReflection);

            if (dxilLayouts.Length != spirvLayouts.Length)
            {
                throw new InvalidDataException("DXIL and SPIRV-Cross report different uniform-buffer record counts.");
            }

            var layoutsByName = new Dictionary<string, ShaderRecordLayout>(StringComparer.Ordinal);

            foreach (var dxilLayout in dxilLayouts)
            {
                if (!layoutsByName.TryAdd(dxilLayout.Name, dxilLayout))
                {
                    throw new InvalidDataException($"DXIL reflection contains duplicate record layout '{dxilLayout.Name}'.");
                }
            }

            foreach (var spirvLayout in spirvLayouts)
            {
                if (!layoutsByName.TryGetValue(spirvLayout.Name, out var dxilLayout)
                    || (dxilLayout.ByteLength != spirvLayout.ByteLength)
                    || (dxilLayout.Fields.Length != spirvLayout.Fields.Length))
                {
                    throw new InvalidDataException($"DXIL record layout '{spirvLayout.Name}' does not match SPIRV-Cross reflection.");
                }

                for (var index = 0; index < dxilLayout.Fields.Length; index++)
                {
                    if (dxilLayout.Fields[index] != spirvLayout.Fields[index])
                    {
                        throw new InvalidDataException($"DXIL record field '{spirvLayout.Fields[index].Name}' does not match SPIRV-Cross reflection.");
                    }
                }
            }
        }

        private static string GetDxilLineContent(string line)
        {
            var trimmed = line.Trim();
            return trimmed.StartsWith(';', StringComparison.Ordinal)
                ? trimmed[1..].Trim()
                : string.Empty;
        }

        private static int GetRecordArrayStride(int byteLength)
            => checked((byteLength + 15) & ~15);

        private static int GetRequiredDxilInteger(string text, string propertyName)
        {
            var propertyIndex = text.IndexOf(propertyName, StringComparison.Ordinal);

            if (propertyIndex < 0)
            {
                throw new InvalidDataException($"DXIL record metadata omitted '{propertyName}'.");
            }

            var value = text.AsSpan(propertyIndex + propertyName.Length);
            var valueIndex = 0;

            while ((valueIndex < value.Length) && char.IsWhiteSpace(value[valueIndex]))
            {
                valueIndex++;
            }

            var valueEnd = valueIndex;

            while ((valueEnd < value.Length) && (value[valueEnd] is >= '0' and <= '9'))
            {
                valueEnd++;
            }

            if ((valueEnd == valueIndex)
                || !int.TryParse(value[valueIndex..valueEnd], NumberStyles.None, CultureInfo.InvariantCulture, out var result))
            {
                throw new InvalidDataException($"DXIL record metadata '{propertyName}' is not a nonnegative 32-bit integer.");
            }

            return result;
        }

        private static ShaderFieldLayout ParseDxilRecordField(string content)
        {
            var separator = content.IndexOf(';', StringComparison.Ordinal);

            if (separator < 0)
            {
                throw new InvalidDataException("A DXIL record field omitted its declaration separator.");
            }

            var declaration = content[..separator].Trim();
            var tokens = declaration.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var typeIndex = 0;
            var isRowMajor = false;

            if ((tokens.Length > 0) && (tokens[0] is "row_major" or "column_major"))
            {
                isRowMajor = StringComparer.Ordinal.Equals(tokens[0], "row_major");
                typeIndex++;
            }

            if (tokens.Length != typeIndex + 2)
            {
                throw new NotSupportedException($"DXIL record field declaration '{declaration}' is outside the supported flat layout.");
            }

            var valueType = ParseDxilRecordValueType(tokens[typeIndex]);

            if (valueType == ShaderValueType.Float4x4 != isRowMajor)
            {
                throw new InvalidDataException("The M04 record layout requires explicitly row-major float4x4 fields.");
            }

            var name = tokens[typeIndex + 1];
            var arrayCount = 1;
            var arrayStart = name.IndexOf('[', StringComparison.Ordinal);

            if (arrayStart >= 0)
            {
                if (!name.EndsWith(']', StringComparison.Ordinal)
                    || (name.IndexOf('[', arrayStart + 1, StringComparison.Ordinal) >= 0))
                {
                    throw new NotSupportedException($"DXIL record field declaration '{declaration}' has an unsupported array shape.");
                }

                var arrayLength = name.AsSpan(arrayStart + 1, name.Length - arrayStart - 2);

                if (!int.TryParse(arrayLength, NumberStyles.None, CultureInfo.InvariantCulture, out arrayCount)
                    || (arrayCount <= 0))
                {
                    throw new InvalidDataException($"DXIL record field declaration '{declaration}' has an invalid array count.");
                }

                name = name[..arrayStart];

                if (arrayCount == 1)
                {
                    throw new NotSupportedException("The reflected record layout cannot distinguish a one-element array from a scalar field.");
                }
            }

            var byteLength = GetTypeByteLength(valueType);
            var arrayStride = (arrayCount == 1) ? 0 : GetRecordArrayStride(byteLength);

            try
            {
                return new ShaderFieldLayout(
                    name,
                    valueType,
                    GetRequiredDxilInteger(content, "Offset:"),
                    byteLength,
                    arrayCount,
                    arrayStride);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"DXIL record field '{name}' has an invalid layout.", exception);
            }
        }

        private static ShaderRecordLayout ParseDxilRecordLayout(string[] lines, ref int index, string cbufferName)
        {
            string? recordName = null;
            int? byteLength = null;
            var fields = ImmutableArray.CreateBuilder<ShaderFieldLayout>();
            var inRecord = false;

            for (var cursor = index + 1; cursor < lines.Length; cursor++)
            {
                var content = GetDxilLineContent(lines[cursor]);

                if (content.StartsWith("cbuffer ", StringComparison.Ordinal)
                    || StringComparer.Ordinal.Equals(content, "Resource Bindings:"))
                {
                    throw new InvalidDataException($"DXIL cbuffer '{cbufferName}' has an unterminated record definition.");
                }

                if (!inRecord)
                {
                    if (content.StartsWith("struct ", StringComparison.Ordinal))
                    {
                        recordName = content["struct ".Length..].Trim();
                        inRecord = true;
                    }

                    continue;
                }

                if (content.StartsWith('}', StringComparison.Ordinal))
                {
                    var separator = content.IndexOf(';', StringComparison.Ordinal);

                    if (separator < 0)
                    {
                        throw new InvalidDataException($"DXIL cbuffer '{cbufferName}' has an invalid record terminator.");
                    }

                    var closingName = content[1..separator].Trim();

                    if (!StringComparer.Ordinal.Equals(recordName, closingName))
                    {
                        throw new InvalidDataException($"DXIL cbuffer '{cbufferName}' has inconsistent record names.");
                    }

                    if (GetRequiredDxilInteger(content, "Offset:") != 0)
                    {
                        throw new InvalidDataException($"DXIL cbuffer '{cbufferName}' has a nonzero record base offset.");
                    }

                    byteLength = GetRequiredDxilInteger(content, "Size:");
                    index = cursor;
                    break;
                }

                if (content.StartsWith("struct ", StringComparison.Ordinal))
                {
                    throw new NotSupportedException("Nested DXIL uniform-buffer records are not supported by the M04 reflection reader.");
                }

                if (content.Contains("Offset:", StringComparison.Ordinal))
                {
                    fields.Add(ParseDxilRecordField(content));
                }
            }

            if (!inRecord || (recordName is null) || (byteLength is null))
            {
                throw new InvalidDataException($"DXIL cbuffer '{cbufferName}' omitted its record definition or byte length.");
            }

            if (!StringComparer.Ordinal.Equals(NormalizeResourceName(cbufferName), NormalizeResourceName(recordName)))
            {
                throw new InvalidDataException($"DXIL cbuffer '{cbufferName}' does not identify its reflected record '{recordName}'.");
            }

            try
            {
                return new ShaderRecordLayout(
                    NormalizeResourceName(recordName),
                    byteLength.Value,
                    fields.ToImmutable());
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"DXIL cbuffer '{cbufferName}' reported an invalid record layout.", exception);
            }
        }

        private static ImmutableArray<ShaderRecordLayout> ParseDxilRecordLayouts(string dxilReflection)
        {
            var lines = dxilReflection.Split('\n');
            var sectionIndex = -1;

            for (var index = 0; index < lines.Length; index++)
            {
                if (StringComparer.Ordinal.Equals(lines[index].Trim(), "; Buffer Definitions:"))
                {
                    sectionIndex = index;
                    break;
                }
            }

            if (sectionIndex < 0)
            {
                return [];
            }

            var layouts = ImmutableArray.CreateBuilder<ShaderRecordLayout>();

            for (var index = sectionIndex + 1; index < lines.Length; index++)
            {
                var content = GetDxilLineContent(lines[index]);

                if (StringComparer.Ordinal.Equals(content, "Resource Bindings:"))
                {
                    break;
                }

                if (content.StartsWith("cbuffer ", StringComparison.Ordinal))
                {
                    layouts.Add(ParseDxilRecordLayout(lines, ref index, content["cbuffer ".Length..].Trim()));
                }
            }

            return layouts.ToImmutable();
        }

        private static ShaderValueType ParseDxilRecordValueType(string type)
            => type switch {
                "float" => ShaderValueType.Float1,
                "float2" => ShaderValueType.Float2,
                "float3" => ShaderValueType.Float3,
                "float4" => ShaderValueType.Float4,
                "float4x4" => ShaderValueType.Float4x4,
                "int" => ShaderValueType.Int1,
                "int2" => ShaderValueType.Int2,
                "int3" => ShaderValueType.Int3,
                "int4" => ShaderValueType.Int4,
                "uint" => ShaderValueType.UInt1,
                "uint2" => ShaderValueType.UInt2,
                "uint3" => ShaderValueType.UInt3,
                "uint4" => ShaderValueType.UInt4,
                _ => throw new NotSupportedException($"DXIL record field type '{type}' is not supported by the M04 reflection reader."),
            };
    }

    private static class SpirvImageTypeReader
    {
        private const uint DecorationBinding = 33;
        private const uint DecorationDescriptorSet = 34;
        private const ushort OpDecorate = 71;
        private const ushort OpTypeArray = 28;
        private const ushort OpTypeFloat = 22;
        private const ushort OpTypeImage = 25;
        private const ushort OpTypeInt = 21;
        private const ushort OpTypePointer = 32;
        private const ushort OpTypeRuntimeArray = 29;
        private const ushort OpVariable = 59;
        private const uint StorageClassUniformConstant = 0;

        internal static string GetCrossDimension(string type, bool isStorage)
            => isStorage
                ? type switch {
                    "image1D" or "iimage1D" or "uimage1D" => "1d",
                    "image2D" or "iimage2D" or "uimage2D" => "2d",
                    "image3D" or "iimage3D" or "uimage3D" => "3d",
                    "imageCube" or "iimageCube" or "uimageCube" => "cube",
                    _ => throw new NotSupportedException($"SPIRV-Cross storage-image type '{type}' has no supported SPIR-V dimension mapping."),
                }
                : type switch {
                    "texture1D" or "itexture1D" or "utexture1D" => "1d",
                    "texture2D" or "itexture2D" or "utexture2D" => "2d",
                    "texture3D" or "itexture3D" or "utexture3D" => "3d",
                    "textureCube" or "itextureCube" or "utextureCube" => "cube",
                    _ => throw new NotSupportedException($"SPIRV-Cross texture type '{type}' has no supported SPIR-V dimension mapping."),
                };

        internal static Dictionary<(int DescriptorSet, int Binding), (string ComponentFormat, bool IsStorage, string Dimension)> Read(
            ReadOnlySpan<byte> bytecode)
        {
            var words = GetSpirvWords(bytecode);
            var scalarFormats = new Dictionary<uint, string>();
            var pointerTypes = new Dictionary<uint, uint>();
            var imageTypes = new Dictionary<uint, (uint SampledTypeId, uint Dimension, uint Sampled)>();
            var arrayElementTypes = new Dictionary<uint, uint>();
            var decorations = new Dictionary<uint, (int? DescriptorSet, int? Binding)>();
            var variables = new List<(uint PointerType, uint Id, uint StorageClass)>();

            for (var cursor = 5; cursor < words.Length;)
            {
                var instruction = words[cursor];
                var wordCount = checked((int)(instruction >> 16));
                var opcode = checked((ushort)(instruction & 0xFFFF));

                if ((wordCount == 0) || (wordCount > words.Length - cursor))
                {
                    throw new InvalidDataException("The reflected SPIR-V module contains a truncated instruction.");
                }

                switch (opcode)
                {
                    case OpTypeInt:
                    {
                        if ((wordCount != 4) || (words[cursor + 3] > 1))
                        {
                            throw new InvalidDataException("SPIR-V OpTypeInt has an invalid operand count or signedness.");
                        }

                        var prefix = (words[cursor + 3] == 0) ? "u" : "i";

                        if (!scalarFormats.TryAdd(words[cursor + 1], $"{prefix}{words[cursor + 2]}"))
                        {
                            throw new InvalidDataException("The reflected SPIR-V module redeclares a type ID.");
                        }

                        break;
                    }

                    case OpTypeFloat:
                    {
                        if (wordCount != 3)
                        {
                            throw new InvalidDataException("SPIR-V OpTypeFloat has an invalid operand count.");
                        }

                        if (!scalarFormats.TryAdd(words[cursor + 1], $"f{words[cursor + 2]}"))
                        {
                            throw new InvalidDataException("The reflected SPIR-V module redeclares a type ID.");
                        }

                        break;
                    }

                    case OpTypeImage:
                    {
                        if ((wordCount is not (9 or 10))
                            || !imageTypes.TryAdd(words[cursor + 1], (words[cursor + 2], words[cursor + 3], words[cursor + 7])))
                        {
                            throw new InvalidDataException("SPIR-V OpTypeImage has an invalid operand count or duplicate result ID.");
                        }

                        break;
                    }

                    case OpTypeArray:
                    {
                        if ((wordCount != 4) || !arrayElementTypes.TryAdd(words[cursor + 1], words[cursor + 2]))
                        {
                            throw new InvalidDataException("SPIR-V OpTypeArray has an invalid operand count or duplicate result ID.");
                        }

                        break;
                    }

                    case OpTypeRuntimeArray:
                    {
                        if ((wordCount != 3) || !arrayElementTypes.TryAdd(words[cursor + 1], words[cursor + 2]))
                        {
                            throw new InvalidDataException("SPIR-V OpTypeRuntimeArray has an invalid operand count or duplicate result ID.");
                        }

                        break;
                    }

                    case OpTypePointer:
                    {
                        if ((wordCount != 4) || !pointerTypes.TryAdd(words[cursor + 1], words[cursor + 3]))
                        {
                            throw new InvalidDataException("SPIR-V OpTypePointer has an invalid operand count or duplicate result ID.");
                        }

                        break;
                    }

                    case OpDecorate:
                    {
                        if (wordCount < 3)
                        {
                            throw new InvalidDataException("SPIR-V OpDecorate has an invalid operand count.");
                        }

                        var targetId = words[cursor + 1];
                        var decoration = words[cursor + 2];

                        if (decoration is not (DecorationBinding or DecorationDescriptorSet))
                        {
                            break;
                        }

                        if (wordCount != 4)
                        {
                            throw new InvalidDataException("SPIR-V descriptor decorations must contain one literal operand.");
                        }

                        var value = words[cursor + 3];

                        if (value > int.MaxValue)
                        {
                            throw new InvalidDataException("SPIR-V descriptor set and binding values must fit in a signed 32-bit integer.");
                        }

                        var existingDecoration = decorations.GetValueOrDefault(targetId);
                        var (descriptorSet, binding) = existingDecoration;

                        if (decoration == DecorationDescriptorSet)
                        {
                            if (descriptorSet is not null)
                            {
                                throw new InvalidDataException("The reflected SPIR-V module has duplicate descriptor-set decorations.");
                            }

                            descriptorSet = (int)value;
                        }
                        else
                        {
                            if (binding is not null)
                            {
                                throw new InvalidDataException("The reflected SPIR-V module has duplicate descriptor-binding decorations.");
                            }

                            binding = (int)value;
                        }

                        decorations[targetId] = (descriptorSet, binding);
                        break;
                    }

                    case OpVariable:
                    {
                        if (wordCount is not (4 or 5))
                        {
                            throw new InvalidDataException("SPIR-V OpVariable has an invalid operand count.");
                        }

                        variables.Add((words[cursor + 1], words[cursor + 2], words[cursor + 3]));
                        break;
                    }
                }

                cursor += wordCount;
            }

            var imageFormats = new Dictionary<(int DescriptorSet, int Binding), (string ComponentFormat, bool IsStorage, string Dimension)>();

            foreach (var (pointerType, variableId, storageClass) in variables)
            {
                if (storageClass != StorageClassUniformConstant)
                {
                    continue;
                }

                if (!pointerTypes.TryGetValue(pointerType, out var typeId))
                {
                    throw new InvalidDataException("A SPIR-V UniformConstant variable has no reflected pointer type.");
                }

                var visitedArrayTypes = new HashSet<uint>();

                while (arrayElementTypes.TryGetValue(typeId, out var elementTypeId))
                {
                    if (!visitedArrayTypes.Add(typeId))
                    {
                        throw new InvalidDataException("The reflected SPIR-V descriptor type contains an array cycle.");
                    }

                    typeId = elementTypeId;
                }

                if (!imageTypes.TryGetValue(typeId, out var imageType))
                {
                    continue;
                }

                if (!decorations.TryGetValue(variableId, out var decoration)
                    || (decoration.DescriptorSet is not int descriptorSet)
                    || (decoration.Binding is not int binding))
                {
                    throw new InvalidDataException("A SPIR-V image variable omitted its descriptor set or binding.");
                }

                if (!scalarFormats.TryGetValue(imageType.SampledTypeId, out var componentFormat)
                    || (componentFormat is not ("f32" or "i32" or "u32")))
                {
                    throw new NotSupportedException("M04 image resources require a 32-bit float, signed-integer, or unsigned-integer component type.");
                }

                var isStorage = imageType.Sampled switch {
                    1 => false,
                    2 => true,
                    _ => throw new NotSupportedException("The M04 reflection reader only converts explicitly sampled or storage images."),
                };
                var dimension = imageType.Dimension switch {
                    0 => "1d",
                    1 => "2d",
                    2 => "3d",
                    3 => "cube",
                    _ => throw new NotSupportedException("The M04 reflection reader only converts 1D, 2D, 3D, and cube images."),
                };

                if (!imageFormats.TryAdd((descriptorSet, binding), (componentFormat, isStorage, dimension)))
                {
                    throw new InvalidDataException("The reflected SPIR-V module contains duplicate image descriptor identities.");
                }
            }

            return imageFormats;
        }
    }

    private readonly record struct ReflectedResource(
        string Name,
        ShaderResourceSignature Signature,
        string SpirvType,
        string? Format,
        string? ComponentFormat);
}
