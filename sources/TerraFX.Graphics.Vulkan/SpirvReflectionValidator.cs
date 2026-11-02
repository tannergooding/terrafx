// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Buffers;
using TerraFX.Graphics;

namespace TerraFX.Graphics.Vulkan;

internal static class SpirvReflectionValidator
{
    private const uint CapabilityMatrix = 0;
    private const uint CapabilityShader = 1;
    private const uint DecorationArrayStride = 6;
    private const uint DecorationBinding = 33;
    private const uint DecorationBlock = 2;
    private const uint DecorationBufferBlock = 3;
    private const uint DecorationDescriptorSet = 34;
    private const uint DecorationMatrixStride = 7;
    private const uint DecorationOffset = 35;
    private const uint ExecutionModelCompute = 5;
    private const uint ExecutionModelFragment = 4;
    private const uint ExecutionModelVertex = 0;
    private const int HeaderWordCount = 5;
    private const uint ImageSampled = 1;
    private const uint ImageStorage = 2;
    private const uint MagicNumber = 0x07230203;
    private const uint MaximumVersion = 0x00010600;
    private const ushort OpCapability = 17;
    private const ushort OpConstant = 43;
    private const ushort OpDecorate = 71;
    private const ushort OpDecorationGroup = 73;
    private const ushort OpEntryPoint = 15;
    private const ushort OpGroupDecorate = 74;
    private const ushort OpGroupMemberDecorate = 75;
    private const ushort OpMemberDecorate = 72;
    private const ushort OpTypeArray = 28;
    private const ushort OpTypeFloat = 22;
    private const ushort OpTypeImage = 25;
    private const ushort OpTypeInt = 21;
    private const ushort OpTypeMatrix = 24;
    private const ushort OpTypePointer = 32;
    private const ushort OpTypeRuntimeArray = 29;
    private const ushort OpTypeSampledImage = 27;
    private const ushort OpTypeSampler = 26;
    private const ushort OpTypeStruct = 30;
    private const ushort OpTypeVector = 23;
    private const ushort OpVariable = 59;
    private const uint StorageClassStorageBuffer = 12;
    private const uint StorageClassUniform = 2;
    private const uint StorageClassUniformConstant = 0;
    private const uint Version1_0 = 0x00010000;
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    internal static void Validate(ShaderBytecodeInput shader, BufferReadLease<byte> bytecode, int index)
    {
        ArgumentNullException.ThrowIfNull(shader);
        ArgumentNullException.ThrowIfNull(bytecode);

        var byteLength = bytecode.Length;

        if ((byteLength <= 0) || ((byteLength % sizeof(uint)) != 0))
        {
            throw new ArgumentException("SPIR-V bytecode length must be a positive multiple of four.", nameof(bytecode));
        }

        if (byteLength > int.MaxValue)
        {
            throw new NotSupportedException("SPIR-V modules larger than 2 GiB are not supported.");
        }

        var words = GC.AllocateUninitializedArray<uint>(checked((int)(byteLength / sizeof(uint))));
        var copyAction = new BytecodeCopyAction(words);
        bytecode.UseWindow(0, checked((int)byteLength), ref copyAction);

        var module = Parse(words);
        _ = FindEntryPoint(module, shader, index);
        var resources = new List<ShaderResourceSignature>();
        ReadResources(module, index, resources);
        CompareResources(shader.Reflection, resources, index);
        ValidateUniformBlockLayouts(module, shader.Reflection, index);
    }

    private static void AddDecoration(Module module, uint target, uint decoration, ReadOnlySpan<uint> operands)
    {
        var value = decoration switch {
            DecorationBlock or DecorationBufferBlock => 1u,
            DecorationArrayStride or DecorationBinding or DecorationDescriptorSet
                => GetDecorationLiteral(operands, target, decoration),
            _ => 0u,
        };
        var targetDecorations = GetDecorations(module, target);

        switch (decoration)
        {
            case DecorationBlock:
            {
                targetDecorations.HasBlock = true;
                break;
            }

            case DecorationBufferBlock:
            {
                targetDecorations.HasBufferBlock = true;
                break;
            }

            case DecorationArrayStride:
            {
                SetUniqueValue(ref targetDecorations.ArrayStride, value, target, decoration);
                break;
            }

            case DecorationBinding:
            {
                SetUniqueValue(ref targetDecorations.Binding, value, target, decoration);
                break;
            }

            case DecorationDescriptorSet:
            {
                SetUniqueValue(ref targetDecorations.DescriptorSet, value, target, decoration);
                break;
            }
        }
    }

    private static void AddMemberDecoration(
        Module module,
        uint structureId,
        uint memberIndex,
        uint decoration,
        ReadOnlySpan<uint> operands)
    {
        if (decoration is not (DecorationMatrixStride or DecorationOffset))
        {
            return;
        }

        var value = GetDecorationLiteral(operands, structureId, decoration);
        var key = new MemberKey(structureId, memberIndex);

        if (!module.MemberDecorations.TryGetValue(key, out var memberDecorations))
        {
            memberDecorations = new MemberDecorations();
            module.MemberDecorations.Add(key, memberDecorations);
        }

        if (decoration == DecorationOffset)
        {
            SetUniqueValue(ref memberDecorations.Offset, value, structureId, decoration);
        }
        else
        {
            SetUniqueValue(ref memberDecorations.MatrixStride, value, structureId, decoration);
        }
    }

    private static void AddType(Module module, ushort opcode, ReadOnlySpan<uint> operands)
    {
        var minimumOperandCount = opcode switch {
            OpTypeInt => 3,
            OpTypeFloat => 2,
            OpTypeSampler => 1,
            OpTypeImage => 8,
            OpTypeSampledImage or OpTypeRuntimeArray => 2,
            OpTypeArray or OpTypePointer or OpTypeVector or OpTypeMatrix => 3,
            OpTypeStruct => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(opcode)),
        };
        RequireOperandCount(operands, minimumOperandCount, "type declaration");
        EnsureId(module, operands[0]);

        if (!module.Types.TryAdd(operands[0], new TypeDeclaration(opcode, operands.ToArray())))
        {
            throw new ArgumentException($"SPIR-V declares duplicate type id {operands[0]}.");
        }
    }

    private static void CompareResources(
        ShaderReflection reflection,
        List<ShaderResourceSignature> actualResources,
        int shaderIndex)
    {
        var expectedResources = reflection.Resources.ToArray();
        var actualResourceArray = actualResources.ToArray();
        Array.Sort(expectedResources, CompareResources);
        Array.Sort(actualResourceArray, CompareResources);

        if (!actualResourceArray.AsSpan().SequenceEqual(expectedResources))
        {
            throw new ArgumentException(
                $"SPIR-V shader input at index {shaderIndex} declares resource bindings that do not match its reflection metadata.",
                nameof(reflection));
        }
    }

    private static int CompareResources(ShaderResourceSignature left, ShaderResourceSignature right)
    {
        var groupComparison = left.Group.CompareTo(right.Group);

        if (groupComparison != 0)
        {
            return groupComparison;
        }

        var classComparison = left.Kind.CompareTo(right.Kind);

        if (classComparison != 0)
        {
            return classComparison;
        }

        var bindingComparison = left.Binding.CompareTo(right.Binding);
        return bindingComparison != 0
            ? bindingComparison
            : left.ArrayCount.CompareTo(right.ArrayCount);
    }

    private static void EnsureId(Module module, uint id)
    {
        if ((id == 0) || (id >= module.IdBound))
        {
            throw new ArgumentException($"SPIR-V id {id} is outside the module id bound.");
        }
    }

    private static EntryPoint FindEntryPoint(Module module, ShaderBytecodeInput shader, int shaderIndex)
    {
        if (module.EntryPoints.Count != 1)
        {
            throw new NotSupportedException("SPIR-V reflection validation requires one entry point per module.");
        }

        var entryPoint = module.EntryPoints[0];

        if (!StringComparer.Ordinal.Equals(entryPoint.Name, shader.Reflection.EntryPoint))
        {
            throw new ArgumentException(
                $"SPIR-V shader input at index {shaderIndex} does not contain its reflected entry point.",
                nameof(shader));
        }

        var expectedExecutionModel = shader.Stage switch {
            ShaderStage.Vertex => ExecutionModelVertex,
            ShaderStage.Pixel => ExecutionModelFragment,
            ShaderStage.Compute => ExecutionModelCompute,
            _ => throw new ArgumentOutOfRangeException(nameof(shader)),
        };

        if (entryPoint.ExecutionModel != expectedExecutionModel)
        {
            throw new ArgumentException(
                $"SPIR-V shader input at index {shaderIndex} has an execution model that does not match its reflected stage.",
                nameof(shader));
        }

        return entryPoint;
    }

    private static uint GetArrayLength(Module module, uint constantId)
    {
        if (!module.Constants.TryGetValue(constantId, out var constant))
        {
            throw new ArgumentException($"SPIR-V descriptor array length id {constantId} is not a constant.");
        }

        var integerType = GetType(module, constant.ResultType);

        if ((integerType.Opcode != OpTypeInt)
            || (integerType.Operands[1] is not (32 or 64))
            || (integerType.Operands[2] > 1)
            || (constant.LiteralWords.Length != (integerType.Operands[1] / 32))
            || (constant.LiteralWords.Length == 0))
        {
            throw new NotSupportedException("SPIR-V descriptor array lengths must be 32-bit or 64-bit integer constants.");
        }

        ulong length = constant.LiteralWords[0];

        if (constant.LiteralWords.Length == 2)
        {
            length |= (ulong)constant.LiteralWords[1] << 32;
        }

        if ((length == 0)
            || (length > int.MaxValue)
            || ((integerType.Operands[2] == 1) && ((length >> checked((int)(integerType.Operands[1] - 1))) != 0)))
        {
            throw new ArgumentOutOfRangeException(nameof(constantId), "SPIR-V descriptor array lengths must be positive and fit in an Int32.");
        }

        return checked((uint)length);
    }

    private static uint GetDecorationLiteral(ReadOnlySpan<uint> operands, uint target, uint decoration)
    {
        if (operands.Length != 1)
        {
            throw new ArgumentException($"SPIR-V decoration {decoration} on id {target} must have one literal operand.");
        }

        return operands[0];
    }

    private static Decorations GetDecorations(Module module, uint target)
    {
        if (!module.Decorations.TryGetValue(target, out var decorations))
        {
            decorations = new Decorations();
            module.Decorations.Add(target, decorations);
        }

        return decorations;
    }

    private static MemberDecorations GetMemberDecorations(Module module, uint structureId, uint memberIndex)
    {
        if (!module.MemberDecorations.TryGetValue(new MemberKey(structureId, memberIndex), out var decorations))
        {
            throw new ArgumentException($"SPIR-V block member {memberIndex} of structure {structureId} has no reflected offset.");
        }

        return decorations;
    }

    private static RecordField GetRecordField(Module module, uint memberTypeId, MemberDecorations memberDecorations)
    {
        if (memberDecorations.Offset is not uint offset)
        {
            throw new ArgumentException("SPIR-V block members must declare an explicit byte offset.");
        }

        var type = GetType(module, memberTypeId);
        var arrayCount = 1;
        var arrayStride = 0;

        if (type.Opcode == OpTypeRuntimeArray)
        {
            throw new NotSupportedException("Runtime-sized SPIR-V block members are not supported.");
        }

        if (type.Opcode == OpTypeArray)
        {
            arrayCount = checked((int)GetArrayLength(module, type.Operands[2]));

            if (arrayCount > 1)
            {
                var decorations = GetDecorations(module, type.Operands[0]);
                arrayStride = checked((int)(decorations.ArrayStride
                    ?? throw new ArgumentException("SPIR-V block arrays must declare an ArrayStride.")));
            }

            type = GetType(module, type.Operands[1]);

            if (type.Opcode is OpTypeArray or OpTypeRuntimeArray)
            {
                throw new NotSupportedException("Nested SPIR-V block arrays are not supported by reflection metadata.");
            }
        }

        var (valueType, byteLength) = GetRecordValueType(module, type, memberDecorations);
        return new RecordField(
            checked((int)offset),
            valueType,
            byteLength,
            arrayCount,
            arrayStride);
    }

    private static (ShaderValueType ValueType, int ByteLength) GetRecordValueType(
        Module module,
        TypeDeclaration type,
        MemberDecorations memberDecorations)
    {
        if (type.Opcode == OpTypeMatrix)
        {
            if (memberDecorations.MatrixStride is null)
            {
                throw new ArgumentException("SPIR-V matrix members must declare a MatrixStride.");
            }

            throw new NotSupportedException(
                "SPIR-V matrix-major layout cannot be compared to shader record metadata that has no major-order field.");
        }

        if (type.Opcode == OpTypeVector)
        {
            var (scalarType, scalarByteLength) = GetScalarValueType(module, type.Operands[1]);
            var componentCount = type.Operands[2];
            var valueType = (scalarType, componentCount) switch {
                (ShaderValueType.Float1, 2) => ShaderValueType.Float2,
                (ShaderValueType.Float1, 3) => ShaderValueType.Float3,
                (ShaderValueType.Float1, 4) => ShaderValueType.Float4,
                (ShaderValueType.Int1, 2) => ShaderValueType.Int2,
                (ShaderValueType.Int1, 3) => ShaderValueType.Int3,
                (ShaderValueType.Int1, 4) => ShaderValueType.Int4,
                (ShaderValueType.UInt1, 2) => ShaderValueType.UInt2,
                (ShaderValueType.UInt1, 3) => ShaderValueType.UInt3,
                (ShaderValueType.UInt1, 4) => ShaderValueType.UInt4,
                _ => throw new NotSupportedException("SPIR-V block vectors must have two, three, or four supported components."),
            };

            return (valueType, checked((int)componentCount * scalarByteLength));
        }

        return GetScalarValueType(module, type.Operands[0]);
    }

    private static ShaderResourceSignature GetResourceBinding(
        ShaderBindingGroup group,
        uint nativeBinding,
        ShaderResourceKind kind,
        int arrayCount,
        int shaderIndex)
    {
        var (bindingClassStart, bindingClassEnd) = kind switch {
            ShaderResourceKind.UniformBuffer => (0u, 3u),
            ShaderResourceKind.SampledImage => (16u, 31u),
            ShaderResourceKind.StorageBuffer or ShaderResourceKind.StorageImage => (32u, 39u),
            ShaderResourceKind.Sampler => (40u, 47u),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        if ((nativeBinding < bindingClassStart) || (nativeBinding > bindingClassEnd))
        {
            throw new ArgumentException(
                $"SPIR-V shader input at index {shaderIndex} uses binding {nativeBinding} outside the reflected {kind} register class.",
                nameof(nativeBinding));
        }

        return new ShaderResourceSignature(
            group,
            checked((int)(nativeBinding - bindingClassStart)),
            kind,
            arrayCount);
    }

    private static ShaderBindingGroup GetResourceGroup(uint setIndex, int shaderIndex)
        => setIndex switch {
            0 => ShaderBindingGroup.Frame,
            1 => ShaderBindingGroup.Material,
            2 => ShaderBindingGroup.ObjectData,
            3 => ShaderBindingGroup.Pass,
            _ => throw new ArgumentException(
                $"SPIR-V shader input at index {shaderIndex} uses descriptor set {setIndex}, outside the supported set range.",
                nameof(setIndex)),
        };

    private static (ShaderResourceKind Kind, int ArrayCount) GetResourceType(
        Module module,
        uint typeId,
        uint storageClass)
    {
        long arrayCount = 1;
        var type = GetType(module, typeId);

        while (type.Opcode is OpTypeArray or OpTypeRuntimeArray)
        {
            if (type.Opcode == OpTypeRuntimeArray)
            {
                throw new NotSupportedException("Runtime-sized SPIR-V descriptor arrays are not supported.");
            }

            arrayCount = checked(arrayCount * GetArrayLength(module, type.Operands[2]));

            if (arrayCount > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(typeId), "The SPIR-V descriptor array exceeds the supported count.");
            }

            type = GetType(module, type.Operands[1]);
        }

        ShaderResourceKind kind;

        if (storageClass == StorageClassUniformConstant)
        {
            kind = type.Opcode switch {
                OpTypeSampler => ShaderResourceKind.Sampler,
                OpTypeImage when type.Operands[6] == ImageSampled => ShaderResourceKind.SampledImage,
                OpTypeImage when type.Operands[6] == ImageStorage => ShaderResourceKind.StorageImage,
                OpTypeImage => throw new NotSupportedException("The SPIR-V image has an unsupported sampled mode."),
                OpTypeSampledImage => throw new NotSupportedException("Combined SPIR-V image-sampler descriptors are not supported."),
                _ => throw new ArgumentException("SPIR-V descriptor metadata disagrees with its UniformConstant type."),
            };
        }
        else if (storageClass == StorageClassStorageBuffer)
        {
            kind = ShaderResourceKind.StorageBuffer;
            ValidateBlockType(module, type, bufferBlock: false);
        }
        else if (storageClass == StorageClassUniform)
        {
            if (type.Opcode != OpTypeStruct)
            {
                throw new ArgumentException("SPIR-V Uniform descriptors must point to a block structure.");
            }

            var decorations = GetDecorations(module, type.Operands[0]);

            if (decorations.HasBufferBlock)
            {
                kind = ShaderResourceKind.StorageBuffer;
            }
            else if (decorations.HasBlock)
            {
                kind = ShaderResourceKind.UniformBuffer;
            }
            else
            {
                throw new ArgumentException("SPIR-V Uniform descriptor structures require Block or BufferBlock decoration.");
            }
        }
        else
        {
            throw new NotSupportedException($"SPIR-V descriptor storage class {storageClass} is not supported.");
        }

        return (kind, checked((int)arrayCount));
    }

    private static (ShaderValueType ValueType, int ByteLength) GetScalarValueType(Module module, uint typeId)
    {
        var type = GetType(module, typeId);

        if ((type.Opcode == OpTypeFloat) && (type.Operands[1] == 32))
        {
            return (ShaderValueType.Float1, sizeof(float));
        }

        if ((type.Opcode == OpTypeInt) && (type.Operands[1] == 32))
        {
            return type.Operands[2] switch {
                0 => (ShaderValueType.UInt1, sizeof(uint)),
                1 => (ShaderValueType.Int1, sizeof(int)),
                _ => throw new ArgumentException("SPIR-V integer signedness must be zero or one."),
            };
        }

        throw new NotSupportedException("SPIR-V block scalar types must be 32-bit floating-point or integer values.");
    }

    private static TypeDeclaration GetType(Module module, uint typeId)
    {
        if (!module.Types.TryGetValue(typeId, out var type))
        {
            throw new ArgumentException($"SPIR-V references missing type id {typeId}.");
        }

        return type;
    }

    private static bool IsDescriptorStorageClass(uint storageClass)
        => storageClass is StorageClassUniformConstant or StorageClassUniform or StorageClassStorageBuffer;

    private static bool MatchesRecordLayout(
        ShaderRecordLayout expected,
        ReadOnlySpan<RecordField> actualFields,
        long actualByteLength)
    {
        if ((expected.ByteLength < actualByteLength) || (expected.Fields.Length != actualFields.Length))
        {
            return false;
        }

        for (var index = 0; index < actualFields.Length; index++)
        {
            var actual = actualFields[index];
            var expectedField = expected.Fields[index];

            if ((expectedField.ByteOffset != actual.ByteOffset)
                || (expectedField.ValueType != actual.ValueType)
                || (expectedField.ByteLength != actual.ByteLength)
                || (expectedField.ArrayCount != actual.ArrayCount)
                || (expectedField.ArrayStride != actual.ArrayStride))
            {
                return false;
            }
        }

        return true;
    }

    private static Module Parse(uint[] words)
    {
        if (words.Length < HeaderWordCount)
        {
            throw new ArgumentException("SPIR-V bytecode is shorter than its five-word header.", nameof(words));
        }

        if ((words[0] != MagicNumber)
            || (words[1] < Version1_0)
            || (words[1] > MaximumVersion)
            || (words[4] != 0)
            || (words[3] == 0))
        {
            throw new ArgumentException("SPIR-V bytecode has an unsupported header.", nameof(words));
        }

        var module = new Module(words[3]);

        for (var cursor = HeaderWordCount; cursor < words.Length;)
        {
            var instruction = words[cursor];
            var wordCount = checked((int)(instruction >> 16));
            var opcode = checked((ushort)(instruction & 0xFFFF));

            if ((wordCount == 0) || (wordCount > words.Length - cursor))
            {
                throw new ArgumentException("SPIR-V bytecode contains a truncated or zero-length instruction.", nameof(words));
            }

            ParseInstruction(module, opcode, words.AsSpan(cursor + 1, wordCount - 1));
            cursor += wordCount;
        }

        if (!module.HasShaderCapability)
        {
            throw new ArgumentException("SPIR-V shader modules must declare the Shader capability.", nameof(words));
        }

        return module;
    }

    private static void ParseInstruction(Module module, ushort opcode, ReadOnlySpan<uint> operands)
    {
        switch (opcode)
        {
            case OpCapability:
            {
                RequireOperandCount(operands, 1, "OpCapability");

                if (operands.Length != 1)
                {
                    throw new ArgumentException("SPIR-V OpCapability has an invalid operand count.");
                }

                if (operands[0] is not (CapabilityMatrix or CapabilityShader))
                {
                    throw new NotSupportedException($"SPIR-V capability {operands[0]} is not implemented by the Vulkan pipeline baseline.");
                }

                module.HasShaderCapability |= operands[0] == CapabilityShader;
                break;
            }

            case OpEntryPoint:
            {
                RequireOperandCount(operands, 3, "OpEntryPoint");
                var name = ReadLiteralString(operands[2..], out var stringWordCount);
                var interfaceStart = checked(2 + stringWordCount);

                if (interfaceStart > operands.Length)
                {
                    throw new ArgumentException("SPIR-V OpEntryPoint has a truncated interface list.");
                }

                var interfaceIds = new HashSet<uint>();

                for (var index = interfaceStart; index < operands.Length; index++)
                {
                    EnsureId(module, operands[index]);

                    if (!interfaceIds.Add(operands[index]))
                    {
                        throw new ArgumentException("SPIR-V OpEntryPoint has a duplicate interface id.");
                    }
                }

                EnsureId(module, operands[1]);
                module.EntryPoints.Add(new EntryPoint(operands[0], name));
                break;
            }

            case OpTypeInt:
            case OpTypeFloat:
            case OpTypeImage:
            case OpTypeSampledImage:
            case OpTypeArray:
            case OpTypeRuntimeArray:
            case OpTypeStruct:
            case OpTypePointer:
            case OpTypeSampler:
            case OpTypeVector:
            case OpTypeMatrix:
            {
                AddType(module, opcode, operands);
                break;
            }

            case OpConstant:
            {
                RequireOperandCount(operands, 3, "OpConstant");
                EnsureId(module, operands[0]);
                EnsureId(module, operands[1]);

                if (!module.Constants.TryAdd(operands[1], new Constant(operands[0], operands[2..].ToArray())))
                {
                    throw new ArgumentException($"SPIR-V declares duplicate constant id {operands[1]}.");
                }

                break;
            }

            case OpVariable:
            {
                RequireOperandCount(operands, 3, "OpVariable");
                EnsureId(module, operands[0]);
                EnsureId(module, operands[1]);

                if (!module.Variables.TryAdd(operands[1], new Variable(operands[0], operands[1], operands[2])))
                {
                    throw new ArgumentException($"SPIR-V declares duplicate variable id {operands[1]}.");
                }

                break;
            }

            case OpDecorate:
            {
                RequireOperandCount(operands, 2, "OpDecorate");
                EnsureId(module, operands[0]);
                AddDecoration(module, operands[0], operands[1], operands[2..]);
                break;
            }

            case OpMemberDecorate:
            {
                RequireOperandCount(operands, 3, "OpMemberDecorate");
                EnsureId(module, operands[0]);
                AddMemberDecoration(module, operands[0], operands[1], operands[2], operands[3..]);
                break;
            }

            case OpDecorationGroup:
            case OpGroupDecorate:
            case OpGroupMemberDecorate:
            {
                throw new NotSupportedException("SPIR-V decoration groups are not supported for runtime reflection validation.");
            }
        }
    }

    private static string ReadLiteralString(ReadOnlySpan<uint> words, out int consumedWordCount)
    {
        var bytes = MemoryMarshal.AsBytes(words);
        var terminator = bytes.IndexOf((byte)0);

        if (terminator < 0)
        {
            throw new ArgumentException("SPIR-V literal string is not null terminated.");
        }

        consumedWordCount = checked((terminator / sizeof(uint)) + 1);
        var consumedBytes = checked(consumedWordCount * sizeof(uint));

        for (var index = terminator + 1; index < consumedBytes; index++)
        {
            if (bytes[index] != 0)
            {
                throw new ArgumentException("SPIR-V literal string has nonzero padding bytes.");
            }
        }

        return s_utf8.GetString(bytes[..terminator]);
    }

    private static void ReadResources(Module module, int shaderIndex, List<ShaderResourceSignature> actualResources)
    {
        foreach (var variable in module.Variables.Values)
        {
            if (!IsDescriptorStorageClass(variable.StorageClass))
            {
                continue;
            }

            var decorations = GetDecorations(module, variable.ResultId);

            if ((decorations.DescriptorSet is not uint setIndex) || (decorations.Binding is not uint binding))
            {
                throw new ArgumentException($"SPIR-V descriptor variable id {variable.ResultId} has an incomplete descriptor binding.");
            }

            var pointerType = GetType(module, variable.ResultType);

            if ((pointerType.Opcode != OpTypePointer)
                || (pointerType.Operands[1] != variable.StorageClass))
            {
                throw new ArgumentException($"SPIR-V descriptor variable id {variable.ResultId} has an incompatible pointer type.");
            }

            var (resourceKind, arrayCount) = GetResourceType(module, pointerType.Operands[2], variable.StorageClass);
            var group = GetResourceGroup(setIndex, shaderIndex);
            actualResources.Add(GetResourceBinding(group, binding, resourceKind, arrayCount, shaderIndex));
        }
    }

    private static void RequireOperandCount(ReadOnlySpan<uint> operands, int minimumCount, string instruction)
    {
        if (operands.Length < minimumCount)
        {
            throw new ArgumentException($"SPIR-V {instruction} has too few operands.");
        }
    }

    private static void SetUniqueValue(ref uint? target, uint value, uint id, uint decoration)
    {
        if (target is uint existing && (existing != value))
        {
            throw new ArgumentException($"SPIR-V id {id} has conflicting decoration {decoration} values.");
        }

        target = value;
    }

    private static void ValidateBlockType(Module module, TypeDeclaration type, bool bufferBlock)
    {
        if (type.Opcode != OpTypeStruct)
        {
            throw new ArgumentException("SPIR-V buffer descriptors must point to a block structure.");
        }

        var decorations = GetDecorations(module, type.Operands[0]);

        if (bufferBlock ? !decorations.HasBufferBlock : !decorations.HasBlock)
        {
            throw new ArgumentException("SPIR-V buffer descriptor structure has no required block decoration.");
        }
    }

    private static void ValidateUniformBlockLayouts(Module module, ShaderReflection reflection, int shaderIndex)
    {
        var visitedStructureIds = new HashSet<uint>();
        var matchedRecordLayouts = new bool[reflection.RecordLayouts.Length];

        foreach (var variable in module.Variables.Values)
        {
            if (variable.StorageClass != StorageClassUniform)
            {
                continue;
            }

            var pointerType = GetType(module, variable.ResultType);

            if (pointerType.Opcode != OpTypePointer)
            {
                throw new ArgumentException($"SPIR-V uniform variable id {variable.ResultId} has no pointer type.");
            }

            var typeId = pointerType.Operands[2];
            var type = GetType(module, typeId);

            while (type.Opcode == OpTypeArray)
            {
                typeId = type.Operands[1];
                type = GetType(module, typeId);
            }

            if (type.Opcode == OpTypeRuntimeArray)
            {
                throw new NotSupportedException("Runtime-sized SPIR-V uniform block arrays are not supported.");
            }

            if (type.Opcode != OpTypeStruct)
            {
                throw new ArgumentException($"SPIR-V uniform variable id {variable.ResultId} does not reference a block structure.");
            }

            if (!GetDecorations(module, typeId).HasBlock || !visitedStructureIds.Add(typeId))
            {
                continue;
            }

            var fields = new RecordField[type.Operands.Length - 1];
            long actualByteLength = 0;

            for (var memberIndex = 0; memberIndex < fields.Length; memberIndex++)
            {
                var memberDecorations = GetMemberDecorations(module, typeId, checked((uint)memberIndex));
                var field = GetRecordField(module, type.Operands[memberIndex + 1], memberDecorations);
                var fieldEnd = checked((long)field.ByteOffset
                    + ((long)(field.ArrayCount - 1) * field.ArrayStride)
                    + field.ByteLength);
                fields[memberIndex] = field;
                actualByteLength = Math.Max(actualByteLength, fieldEnd);
            }

            Array.Sort(fields, static (left, right) => left.ByteOffset.CompareTo(right.ByteOffset));

            var matched = false;

            for (var recordIndex = 0; recordIndex < reflection.RecordLayouts.Length; recordIndex++)
            {
                if (!matchedRecordLayouts[recordIndex]
                    && MatchesRecordLayout(reflection.RecordLayouts[recordIndex], fields, actualByteLength))
                {
                    matchedRecordLayouts[recordIndex] = true;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                throw new ArgumentException(
                    $"SPIR-V shader input at index {shaderIndex} declares a uniform block layout that does not match its reflection metadata.",
                    nameof(reflection));
            }
        }
    }

    private readonly struct BytecodeCopyAction : IAction<ReadOnlySpan<byte>>
    {
        private readonly uint[] _destination;

        internal BytecodeCopyAction(uint[] destination)
        {
            _destination = destination;
        }

        public readonly void Invoke(scoped ReadOnlySpan<byte> argument)
            => argument.CopyTo(MemoryMarshal.AsBytes(_destination.AsSpan()));
    }

    private readonly record struct Constant(uint ResultType, uint[] LiteralWords);

    private sealed class Decorations
    {
        internal uint? ArrayStride;
        internal uint? Binding;
        internal uint? DescriptorSet;
        internal bool HasBlock;
        internal bool HasBufferBlock;
    }

    private sealed class EntryPoint
    {
        internal EntryPoint(uint executionModel, string name)
        {
            ExecutionModel = executionModel;
            Name = name;
        }

        internal uint ExecutionModel { get; }

        internal string Name { get; }
    }

    private sealed class MemberDecorations
    {
        internal uint? MatrixStride;
        internal uint? Offset;
    }

    private readonly record struct MemberKey(uint StructureId, uint MemberIndex);

    private sealed class Module
    {
        internal bool HasShaderCapability;

        internal Module(uint idBound)
        {
            IdBound = idBound;
        }

        internal Dictionary<uint, Constant> Constants { get; } = [];

        internal Dictionary<uint, Decorations> Decorations { get; } = [];

        internal List<EntryPoint> EntryPoints { get; } = [];

        internal uint IdBound { get; }

        internal Dictionary<MemberKey, MemberDecorations> MemberDecorations { get; } = [];

        internal Dictionary<uint, TypeDeclaration> Types { get; } = [];

        internal Dictionary<uint, Variable> Variables { get; } = [];
    }

    private readonly record struct RecordField(
        int ByteOffset,
        ShaderValueType ValueType,
        int ByteLength,
        int ArrayCount,
        int ArrayStride);

    private sealed class TypeDeclaration
    {
        internal TypeDeclaration(ushort opcode, uint[] operands)
        {
            Opcode = opcode;
            Operands = operands;
        }

        internal ushort Opcode { get; }

        internal uint[] Operands { get; }
    }

    private readonly record struct Variable(uint ResultType, uint ResultId, uint StorageClass);
}
