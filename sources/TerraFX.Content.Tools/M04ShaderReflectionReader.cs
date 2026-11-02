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

        var resources = ParseResources(root);
        ValidateDxilResourceBindings(dxilReflection, resources);
        var inputs = ParseDxilSignatures(dxilReflection, "Input signature:", stage, isInput: true);
        var outputs = ParseDxilSignatures(dxilReflection, "Output signature:", stage, isInput: false);
        ValidateSpirvCrossIo(root, "inputs", inputs, stage, isInput: true);
        ValidateSpirvCrossIo(root, "outputs", outputs, stage, isInput: false);
        var recordLayouts = ParseRecordLayouts(root);
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
                format));
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
        if ((bytecode.Length < (5 * sizeof(uint))) || ((bytecode.Length % sizeof(uint)) != 0))
        {
            throw new InvalidDataException("The reflected SPIR-V module has an invalid byte length.");
        }

        var words = MemoryMarshal.Cast<byte, uint>(bytecode);

        if (words[0] != MagicNumber)
        {
            throw new InvalidDataException("The reflected shader bytecode is not SPIR-V.");
        }

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
                var valueType = ParseSpirvType(GetRequiredString(member, "type"));
                var arrayCount = GetArrayCount(member);
                var arrayStride = (arrayCount == 1) ? 0 : GetRequiredInt32(member, "array_stride");
                fields.Add(new ShaderFieldLayout(
                    GetRequiredString(member, "name"),
                    valueType,
                    GetRequiredInt32(member, "offset"),
                    GetTypeByteLength(valueType),
                    arrayCount,
                    arrayStride));
            }

            var typeName = type.TryGetProperty("name", out _)
                ? GetRequiredString(type, "name")
                : GetRequiredString(uniformBuffer, "name");
            layouts.Add(new ShaderRecordLayout(
                NormalizeResourceName(typeName),
                GetRequiredInt32(uniformBuffer, "block_size"),
                fields.MoveToImmutable()));
        }

        return layouts.MoveToImmutable();
    }

    private static ImmutableArray<ReflectedResource> ParseResources(JsonElement root)
    {
        CheckUnsupportedResourceCollections(root);
        var resources = ImmutableArray.CreateBuilder<ReflectedResource>();
        AddResourceCollection(root, "ubos", ShaderResourceKind.UniformBuffer, resources);
        AddResourceCollection(root, "separate_images", ShaderResourceKind.SampledImage, resources);
        AddResourceCollection(root, "separate_samplers", ShaderResourceKind.Sampler, resources);
        AddResourceCollection(root, "ssbos", ShaderResourceKind.StorageBuffer, resources);
        AddResourceCollection(root, "images", ShaderResourceKind.StorageImage, resources);
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
        static string GetTextureDimension(string type)
        {
            return type switch {
                "texture1D" or "itexture1D" or "utexture1D" => "1d",
                "texture2D" or "itexture2D" or "utexture2D" => "2d",
                "texture3D" or "itexture3D" or "utexture3D" => "3d",
                "textureCube" or "itextureCube" or "utextureCube" => "cube",
                _ => throw new NotSupportedException($"SPIRV-Cross texture type '{type}' has no supported DXIL dimension mapping."),
            };
        }

        static string GetStorageImageDimension(string type)
        {
            return type switch {
                "image1D" or "iimage1D" or "uimage1D" => "1d",
                "image2D" or "iimage2D" or "uimage2D" => "2d",
                "image3D" or "iimage3D" or "uimage3D" => "3d",
                "imageCube" or "iimageCube" or "uimageCube" => "cube",
                _ => throw new NotSupportedException($"SPIRV-Cross storage-image type '{type}' has no supported DXIL dimension mapping."),
            };
        }

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
                ShaderResourceKind.SampledImage => ("texture", null, GetTextureDimension(resource.SpirvType)),
                ShaderResourceKind.StorageBuffer => ("UAV", "struct", "r/w"),
                ShaderResourceKind.StorageImage => ("UAV", GetDxilStorageImageFormat(resource.Format), GetStorageImageDimension(resource.SpirvType)),
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

    private readonly record struct ReflectedResource(
        string Name,
        ShaderResourceSignature Signature,
        string SpirvType,
        string? Format);
}
