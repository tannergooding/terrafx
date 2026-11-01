// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Contains copied reflection metadata for one compiled shader stage.</summary>
public sealed class ShaderReflection
{
    private readonly ImmutableArray<ShaderIoSignature> _inputs;
    private readonly ImmutableArray<ShaderIoSignature> _outputs;
    private readonly ImmutableArray<ShaderRecordLayout> _recordLayouts;
    private readonly ImmutableArray<ShaderCapability> _requiredCapabilities;
    private readonly ImmutableArray<ShaderResourceSignature> _resources;

    /// <summary>Initializes copied shader reflection metadata.</summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="entryPoint">The nonempty entry-point name.</param>
    /// <param name="compilerIdentity">The exact offline compiler package identity.</param>
    /// <param name="inputs">The initialized stage inputs.</param>
    /// <param name="outputs">The initialized stage outputs.</param>
    /// <param name="resources">The initialized resource bindings.</param>
    /// <param name="recordLayouts">The initialized record layouts.</param>
    /// <param name="requiredCapabilities">The initialized required capabilities.</param>
    /// <param name="bindingLayoutVersion">The positive binding-layout version.</param>
    /// <param name="groupSizeX">The compute group size in X, or zero for graphics stages.</param>
    /// <param name="groupSizeY">The compute group size in Y, or zero for graphics stages.</param>
    /// <param name="groupSizeZ">The compute group size in Z, or zero for graphics stages.</param>
    /// <exception cref="ArgumentException">A stage array is default, contains duplicate or invalid entries, or conflicts with the stage.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A stage, version, or group-size value is invalid.</exception>
    public ShaderReflection(
        ShaderStage stage,
        string entryPoint,
        ShaderCompilerIdentity compilerIdentity,
        ImmutableArray<ShaderIoSignature> inputs,
        ImmutableArray<ShaderIoSignature> outputs,
        ImmutableArray<ShaderResourceSignature> resources,
        ImmutableArray<ShaderRecordLayout> recordLayouts,
        ImmutableArray<ShaderCapability> requiredCapabilities,
        int bindingLayoutVersion,
        int groupSizeX,
        int groupSizeY,
        int groupSizeZ)
    {
        if ((stage == ShaderStage.None) || !Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        if (!compilerIdentity.IsValid)
        {
            throw new ArgumentException("Shader reflection requires a complete compiler package identity.", nameof(compilerIdentity));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bindingLayoutVersion);

        if (inputs.IsDefault
            || outputs.IsDefault
            || resources.IsDefault
            || recordLayouts.IsDefault
            || requiredCapabilities.IsDefault)
        {
            throw new ArgumentException("Shader reflection arrays must be initialized.");
        }

        if (stage == ShaderStage.Compute)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupSizeX);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupSizeY);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(groupSizeZ);

            if ((groupSizeX > 256)
                || (groupSizeY > 256)
                || (groupSizeZ > 256)
                || ((long)groupSizeX * groupSizeY * groupSizeZ > 256))
            {
                throw new ArgumentOutOfRangeException(nameof(groupSizeX), "The portable compute work-group product cannot exceed 256 invocations.");
            }

            if (!inputs.IsEmpty || !outputs.IsEmpty)
            {
                throw new ArgumentException("Compute shader reflection cannot contain stage input or output signatures.");
            }
        }
        else if ((groupSizeX != 0) || (groupSizeY != 0) || (groupSizeZ != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(groupSizeX), "Graphics shader reflection must have zero compute work-group sizes.");
        }

        var sortedInputs = inputs.ToArray();
        Array.Sort(sortedInputs, CompareIo);
        ValidateIo(sortedInputs, nameof(inputs));
        var sortedOutputs = outputs.ToArray();
        Array.Sort(sortedOutputs, CompareIo);
        ValidateIo(sortedOutputs, nameof(outputs));
        var sortedResources = resources.ToArray();
        ValidateResources(sortedResources);
        Array.Sort(sortedResources, CompareResources);
        ValidateUniqueResources(sortedResources);
        var sortedRecordLayouts = recordLayouts.ToArray();
        ValidateNotNull(sortedRecordLayouts, nameof(recordLayouts));
        Array.Sort(sortedRecordLayouts, static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        ValidateRecordLayouts(sortedRecordLayouts);
        var sortedCapabilities = requiredCapabilities.ToArray();
        Array.Sort(sortedCapabilities);
        ValidateCapabilities(sortedCapabilities);

        Stage = stage;
        EntryPoint = entryPoint;
        CompilerIdentity = compilerIdentity;
        BindingLayoutVersion = bindingLayoutVersion;
        GroupSizeX = groupSizeX;
        GroupSizeY = groupSizeY;
        GroupSizeZ = groupSizeZ;
        _inputs = [.. sortedInputs];
        _outputs = [.. sortedOutputs];
        _resources = [.. sortedResources];
        _recordLayouts = [.. sortedRecordLayouts];
        _requiredCapabilities = [.. sortedCapabilities];
    }

    /// <summary>Gets the positive binding-layout version.</summary>
    public int BindingLayoutVersion { get; }

    /// <summary>Gets the exact offline compiler package identity.</summary>
    public ShaderCompilerIdentity CompilerIdentity { get; }

    /// <summary>Gets the entry-point name.</summary>
    public string EntryPoint { get; }

    /// <summary>Gets the compute work-group size in X, or zero for graphics stages.</summary>
    public int GroupSizeX { get; }

    /// <summary>Gets the compute work-group size in Y, or zero for graphics stages.</summary>
    public int GroupSizeY { get; }

    /// <summary>Gets the compute work-group size in Z, or zero for graphics stages.</summary>
    public int GroupSizeZ { get; }

    /// <summary>Gets copied stage inputs in canonical location order.</summary>
    public ImmutableArray<ShaderIoSignature> Inputs => _inputs;

    /// <summary>Gets copied stage outputs in canonical location order.</summary>
    public ImmutableArray<ShaderIoSignature> Outputs => _outputs;

    /// <summary>Gets copied record layouts in ordinal name order.</summary>
    public ImmutableArray<ShaderRecordLayout> RecordLayouts => _recordLayouts;

    /// <summary>Gets copied required capabilities in numeric order.</summary>
    public ImmutableArray<ShaderCapability> RequiredCapabilities => _requiredCapabilities;

    /// <summary>Gets copied resources in canonical group/class/binding order.</summary>
    public ImmutableArray<ShaderResourceSignature> Resources => _resources;

    /// <summary>Gets the shader stage.</summary>
    public ShaderStage Stage { get; }

    internal static int CompareResourceIdentity(ShaderResourceSignature left, ShaderResourceSignature right)
    {
        var groupComparison = left.Group.CompareTo(right.Group);

        if (groupComparison != 0)
        {
            return groupComparison;
        }

        var classComparison = GetResourceClass(left.Kind).CompareTo(GetResourceClass(right.Kind));
        return (classComparison != 0)
            ? classComparison
            : left.Binding.CompareTo(right.Binding);
    }

    private static int CompareIo(ShaderIoSignature left, ShaderIoSignature right)
    {
        var locationComparison = CompareNullableLocations(left.Location, right.Location);

        if (locationComparison != 0)
        {
            return locationComparison;
        }

        var semanticComparison = StringComparer.Ordinal.Compare(left.SemanticName, right.SemanticName);
        return (semanticComparison != 0)
            ? semanticComparison
            : left.SemanticIndex.CompareTo(right.SemanticIndex);
    }

    private static int CompareNullableLocations(int? left, int? right)
    {
        if (left is null)
        {
            return (right is null) ? 0 : 1;
        }

        return (right is null) ? -1 : left.Value.CompareTo(right.Value);
    }

    private static int CompareResources(ShaderResourceSignature left, ShaderResourceSignature right)
    {
        var identityComparison = CompareResourceIdentity(left, right);
        return (identityComparison != 0)
            ? identityComparison
            : left.Kind.CompareTo(right.Kind);
    }

    private static int GetResourceClass(ShaderResourceKind kind)
        => kind switch {
            ShaderResourceKind.UniformBuffer => 0,
            ShaderResourceKind.SampledImage => 1,
            ShaderResourceKind.StorageImage or ShaderResourceKind.StorageBuffer => 2,
            ShaderResourceKind.Sampler => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static void ValidateCapabilities(ReadOnlySpan<ShaderCapability> capabilities)
    {
        for (var index = 0; index < capabilities.Length; index++)
        {
            if ((capabilities[index] == ShaderCapability.None) || !Enum.IsDefined(capabilities[index]))
            {
                throw new ArgumentOutOfRangeException(nameof(capabilities));
            }

            if ((index > 0) && (capabilities[index] == capabilities[index - 1]))
            {
                throw new ArgumentException("Shader capability requirements must be unique.", nameof(capabilities));
            }
        }
    }

    private static void ValidateIo(ReadOnlySpan<ShaderIoSignature> signatures, string parameterName)
    {
        for (var index = 0; index < signatures.Length; index++)
        {
            var signature = signatures[index];

            if (string.IsNullOrWhiteSpace(signature.SemanticName)
                || (signature.SemanticIndex < 0)
                || (signature.ValueType == ShaderValueType.None)
                || !Enum.IsDefined(signature.ValueType)
                || (signature.Interpolation == ShaderInterpolation.None)
                || !Enum.IsDefined(signature.Interpolation)
                || signature.Location is < 0
                || ((signature.Location is null) && !signature.SemanticName.StartsWith("SV_", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Shader stage I/O signatures must contain valid initialized values.", parameterName);
            }

            if (index == 0)
            {
                continue;
            }

            var previous = signatures[index - 1];

            if ((signature.Location is int location) && (previous.Location == location))
            {
                throw new ArgumentException("Shader stage I/O locations must be unique.", parameterName);
            }

            if ((signature.Location is null)
                && (previous.Location is null)
                && StringComparer.Ordinal.Equals(signature.SemanticName, previous.SemanticName)
                && (signature.SemanticIndex == previous.SemanticIndex))
            {
                throw new ArgumentException("System-value stage I/O semantics must be unique.", parameterName);
            }
        }
    }

    private static void ValidateNotNull<T>(ReadOnlySpan<T?> values, string parameterName)
        where T : class
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] is null)
            {
                throw new ArgumentException("Shader reflection arrays cannot contain null values.", parameterName);
            }
        }
    }

    private static void ValidateRecordLayouts(ReadOnlySpan<ShaderRecordLayout> recordLayouts)
    {
        for (var index = 0; index < recordLayouts.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(recordLayouts[index].Name)
                || (recordLayouts[index].ByteLength <= 0)
                || recordLayouts[index].Fields.IsDefaultOrEmpty)
            {
                throw new ArgumentException("Shader record layouts must be initialized and valid.", nameof(recordLayouts));
            }

            if ((index > 0) && StringComparer.Ordinal.Equals(recordLayouts[index].Name, recordLayouts[index - 1].Name))
            {
                throw new ArgumentException("Shader record layout names must be unique.", nameof(recordLayouts));
            }
        }
    }

    private static void ValidateResources(ReadOnlySpan<ShaderResourceSignature> resources)
    {
        for (var index = 0; index < resources.Length; index++)
        {
            var resource = resources[index];

            if ((resource.Group == ShaderBindingGroup.None)
                || !Enum.IsDefined(resource.Group)
                || (resource.Kind == ShaderResourceKind.None)
                || !Enum.IsDefined(resource.Kind)
                || (resource.ArrayCount <= 0)
                || (resource.Binding < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(resources));
            }

            var maximumBinding = resource.Kind switch {
                ShaderResourceKind.UniformBuffer => 3,
                ShaderResourceKind.SampledImage => 15,
                ShaderResourceKind.StorageImage or ShaderResourceKind.StorageBuffer => 7,
                ShaderResourceKind.Sampler => 7,
                _ => throw new ArgumentOutOfRangeException(nameof(resources)),
            };

            if (resource.Binding > maximumBinding)
            {
                throw new ArgumentOutOfRangeException(nameof(resources));
            }
        }
    }

    private static void ValidateUniqueResources(ReadOnlySpan<ShaderResourceSignature> resources)
    {
        for (var index = 1; index < resources.Length; index++)
        {
            var resource = resources[index];
            var previous = resources[index - 1];

            if ((resource.Group == previous.Group)
                && (GetResourceClass(resource.Kind) == GetResourceClass(previous.Kind))
                && (resource.Binding == previous.Binding))
            {
                throw new ArgumentException("Shader resource register identities must be unique.", nameof(resources));
            }
        }
    }
}
