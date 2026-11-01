// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Describes one graphics pipeline and its reflected shader stages.</summary>
public sealed class GraphicsPipelineDescription : PipelineDescription
{
    private readonly ImmutableArray<TextureFormat> _colorTargetFormats;
    private readonly ImmutableArray<ShaderBytecodeInput> _shaders;
    private readonly ImmutableArray<VertexElementDescription> _vertexElements;

    /// <summary>Initializes a graphics pipeline description.</summary>
    /// <param name="shaders">The vertex and pixel shader inputs.</param>
    /// <param name="vertexElements">The vertex elements matching non-system vertex-shader inputs.</param>
    /// <param name="vertexStride">The vertex record size, or zero when no vertex buffer is used.</param>
    /// <param name="colorTargetFormats">The ordered color-target formats.</param>
    /// <param name="depthTargetFormat">The depth-target format, when depth testing is enabled.</param>
    /// <param name="topology">The primitive topology.</param>
    /// <param name="blendMode">The fixed color-blend mode.</param>
    /// <param name="depthMode">The fixed reversed-Z depth mode.</param>
    /// <param name="cullMode">The rasterizer cull mode.</param>
    /// <param name="frontFaceClockwise">Whether clockwise triangles are front-facing.</param>
    /// <param name="bindingLayoutVersion">The positive binding-layout version.</param>
    /// <exception cref="ArgumentException">The shader stages, reflected inputs, render targets, or vertex layout are inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A render state, format, stride, or binding-layout version is invalid.</exception>
    public GraphicsPipelineDescription(
        ImmutableArray<ShaderBytecodeInput> shaders,
        ImmutableArray<VertexElementDescription> vertexElements,
        int vertexStride,
        ImmutableArray<TextureFormat> colorTargetFormats,
        TextureFormat? depthTargetFormat,
        PrimitiveTopology topology,
        PipelineBlendMode blendMode,
        PipelineDepthMode depthMode,
        PipelineCullMode cullMode,
        bool frontFaceClockwise,
        int bindingLayoutVersion)
        : base(bindingLayoutVersion)
    {
        if (shaders.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Graphics pipeline shader inputs must be initialized and nonempty.", nameof(shaders));
        }

        if (vertexElements.IsDefault)
        {
            throw new ArgumentException("Graphics pipeline vertex elements must be initialized.", nameof(vertexElements));
        }

        if (colorTargetFormats.IsDefault)
        {
            throw new ArgumentException("Graphics pipeline color-target formats must be initialized.", nameof(colorTargetFormats));
        }

        if (!Enum.IsDefined(topology))
        {
            throw new ArgumentOutOfRangeException(nameof(topology));
        }

        if (!Enum.IsDefined(blendMode))
        {
            throw new ArgumentOutOfRangeException(nameof(blendMode));
        }

        if (!Enum.IsDefined(depthMode))
        {
            throw new ArgumentOutOfRangeException(nameof(depthMode));
        }

        if (!Enum.IsDefined(cullMode))
        {
            throw new ArgumentOutOfRangeException(nameof(cullMode));
        }

        ShaderBytecodeInput? vertexShader = null;
        ShaderBytecodeInput? pixelShader = null;

        for (var index = 0; index < shaders.Length; index++)
        {
            var shader = shaders[index]
                ?? throw new ArgumentException("Graphics pipeline shader inputs cannot contain null values.", nameof(shaders));

            if (shader.Reflection.BindingLayoutVersion != bindingLayoutVersion)
            {
                throw new ArgumentException("Every shader reflection record must use the pipeline binding-layout version.", nameof(shaders));
            }

            switch (shader.Stage)
            {
                case ShaderStage.Vertex when vertexShader is null:
                {
                    vertexShader = shader;
                    break;
                }

                case ShaderStage.Pixel when pixelShader is null:
                {
                    pixelShader = shader;
                    break;
                }

                case ShaderStage.Vertex or ShaderStage.Pixel:
                {
                    throw new ArgumentException("Graphics pipelines require exactly one shader for each graphics stage.", nameof(shaders));
                }

                default:
                {
                    throw new ArgumentException("Graphics pipelines accept only vertex and pixel shader stages.", nameof(shaders));
                }
            }
        }

        if ((vertexShader is null) || (pixelShader is null))
        {
            throw new ArgumentException("Graphics pipelines require exactly one vertex shader and one pixel shader.", nameof(shaders));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(vertexStride);

        var sortedVertexElements = vertexElements.ToArray();
        Array.Sort(sortedVertexElements, static (left, right) => left.Location.CompareTo(right.Location));
        ValidateVertexElements(sortedVertexElements, vertexShader.Reflection.Inputs, vertexStride);
        ValidateStageLinkage(vertexShader.Reflection, pixelShader.Reflection);
        ValidateStageResourceBindings(vertexShader.Reflection, pixelShader.Reflection);

        var copiedColorFormats = colorTargetFormats.ToArray();

        for (var index = 0; index < copiedColorFormats.Length; index++)
        {
            var format = copiedColorFormats[index];

            if (format == TextureFormat.None || !Enum.IsDefined(format) || format == TextureFormat.D32Float)
            {
                throw new ArgumentOutOfRangeException(nameof(colorTargetFormats), "Color targets require a defined color format.");
            }
        }

        ValidatePixelTargets(pixelShader.Reflection, copiedColorFormats, depthTargetFormat, depthMode);

        var depthIsEnabled = depthMode != PipelineDepthMode.Disabled;

        if (depthIsEnabled != depthTargetFormat.HasValue)
        {
            throw new ArgumentException("Depth mode and depth-target format must agree.", nameof(depthTargetFormat));
        }

        if (depthTargetFormat is TextureFormat selectedDepthFormat)
        {
            if (!Enum.IsDefined(selectedDepthFormat) || (selectedDepthFormat != TextureFormat.D32Float))
            {
                throw new ArgumentOutOfRangeException(nameof(depthTargetFormat), "The selected depth format must be D32Float.");
            }
        }

        _colorTargetFormats = [.. copiedColorFormats];
        _shaders = [vertexShader, pixelShader];
        _vertexElements = [.. sortedVertexElements];
        BlendMode = blendMode;
        CullMode = cullMode;
        DepthMode = depthMode;
        DepthTargetFormat = depthTargetFormat;
        FrontFaceClockwise = frontFaceClockwise;
        Topology = topology;
        VertexStride = vertexStride;
    }

    /// <summary>Gets the fixed color-blend mode.</summary>
    public PipelineBlendMode BlendMode { get; }

    /// <summary>Gets the ordered color-target formats.</summary>
    public ImmutableArray<TextureFormat> ColorTargetFormats => _colorTargetFormats;

    /// <summary>Gets the rasterizer cull mode.</summary>
    public PipelineCullMode CullMode { get; }

    /// <summary>Gets the fixed reversed-Z depth mode.</summary>
    public PipelineDepthMode DepthMode { get; }

    /// <summary>Gets the depth-target format, or null when depth testing is disabled.</summary>
    public TextureFormat? DepthTargetFormat { get; }

    /// <summary>Gets whether clockwise triangles are front-facing.</summary>
    public bool FrontFaceClockwise { get; }

    /// <summary>Gets the vertex and pixel shader inputs in canonical stage order.</summary>
    public ImmutableArray<ShaderBytecodeInput> Shaders => _shaders;

    /// <summary>Gets the primitive topology.</summary>
    public PrimitiveTopology Topology { get; }

    /// <summary>Gets the vertex elements in ascending input-location order.</summary>
    public ImmutableArray<VertexElementDescription> VertexElements => _vertexElements;

    /// <summary>Gets the vertex record size, or zero when no vertex buffer is used.</summary>
    public int VertexStride { get; }

    private static void ValidatePixelTargets(
        ShaderReflection pixelReflection,
        ReadOnlySpan<TextureFormat> colorTargetFormats,
        TextureFormat? depthTargetFormat,
        PipelineDepthMode depthMode)
    {
        var colorTargetWrites = new bool[colorTargetFormats.Length];
        var hasDepthOutput = false;

        foreach (var output in pixelReflection.Outputs)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(output.SemanticName, "SV_Target"))
            {
                if ((uint)output.SemanticIndex >= (uint)colorTargetFormats.Length)
                {
                    throw new ArgumentException("Each SV_Target output must have a matching color-target format.", nameof(colorTargetFormats));
                }

                if (colorTargetWrites[output.SemanticIndex])
                {
                    throw new ArgumentException("A pixel shader cannot write the same color target more than once.", nameof(pixelReflection));
                }

                colorTargetWrites[output.SemanticIndex] = true;
                var targetFormat = colorTargetFormats[output.SemanticIndex];
                var isUnsignedOutput = output.ValueType is ShaderValueType.UInt1 or ShaderValueType.UInt2 or ShaderValueType.UInt3 or ShaderValueType.UInt4;

                if (targetFormat == TextureFormat.R32UInt != isUnsignedOutput)
                {
                    throw new ArgumentException("The pixel-shader output value type must match its color-target numeric class.", nameof(colorTargetFormats));
                }

                if (!isUnsignedOutput
                    && (output.ValueType is not (ShaderValueType.Float1 or ShaderValueType.Float2 or ShaderValueType.Float3 or ShaderValueType.Float4)))
                {
                    throw new ArgumentException("Color-target outputs must use floating-point or unsigned integer values.", nameof(pixelReflection));
                }
            }
            else if (StringComparer.OrdinalIgnoreCase.Equals(output.SemanticName, "SV_Depth"))
            {
                if (hasDepthOutput)
                {
                    throw new ArgumentException("A pixel shader cannot write SV_Depth more than once.", nameof(pixelReflection));
                }

                hasDepthOutput = true;

                if ((output.ValueType != ShaderValueType.Float1)
                    || (depthMode == PipelineDepthMode.Disabled)
                    || (depthTargetFormat != TextureFormat.D32Float))
                {
                    throw new ArgumentException("An SV_Depth output requires a scalar float and an enabled D32Float depth target.", nameof(depthTargetFormat));
                }
            }
        }
    }

    private static void ValidateStageLinkage(ShaderReflection vertexReflection, ShaderReflection pixelReflection)
    {
        foreach (var pixelInput in pixelReflection.Inputs)
        {
            if (pixelInput.Location is not int location)
            {
                continue;
            }

            var matchingOutput = false;

            foreach (var vertexOutput in vertexReflection.Outputs)
            {
                if (vertexOutput.Location == location)
                {
                    if (vertexOutput.ValueType != pixelInput.ValueType)
                    {
                        throw new ArgumentException("Vertex output and pixel input types must agree at each user location.", nameof(pixelReflection));
                    }

                    matchingOutput = true;
                    break;
                }
            }

            if (!matchingOutput)
            {
                throw new ArgumentException("Every user pixel-shader input must have a matching vertex-shader output.", nameof(pixelReflection));
            }
        }
    }

    private static void ValidateStageResourceBindings(ShaderReflection vertexReflection, ShaderReflection pixelReflection)
    {
        var vertexResources = vertexReflection.Resources;
        var pixelResources = pixelReflection.Resources;
        var vertexIndex = 0;
        var pixelIndex = 0;

        while ((vertexIndex < vertexResources.Length) && (pixelIndex < pixelResources.Length))
        {
            var vertexResource = vertexResources[vertexIndex];
            var pixelResource = pixelResources[pixelIndex];
            var identityComparison = ShaderReflection.CompareResourceIdentity(vertexResource, pixelResource);

            if (identityComparison < 0)
            {
                vertexIndex++;
                continue;
            }

            if (identityComparison > 0)
            {
                pixelIndex++;
                continue;
            }

            if ((vertexResource.Kind != pixelResource.Kind)
                || (vertexResource.ArrayCount != pixelResource.ArrayCount))
            {
                throw new ArgumentException(
                    "Vertex and pixel shaders must agree on the type and descriptor count of each shared resource binding.",
                    nameof(pixelReflection));
            }

            vertexIndex++;
            pixelIndex++;
        }
    }

    private static void ValidateVertexElements(
        ReadOnlySpan<VertexElementDescription> vertexElements,
        ImmutableArray<ShaderIoSignature> vertexInputs,
        int vertexStride)
    {
        var inputIndex = 0;

        for (var index = 0; index < vertexInputs.Length; index++)
        {
            if (vertexInputs[index].Location is not null)
            {
                inputIndex++;
            }
        }

        if (vertexElements.Length != inputIndex)
        {
            throw new ArgumentException("The vertex layout must contain exactly one element for each non-system vertex input.", nameof(vertexElements));
        }

        if (vertexElements.IsEmpty)
        {
            if (vertexStride != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexStride), "A pipeline without vertex elements cannot declare a vertex stride.");
            }

            for (var index = 0; index < vertexInputs.Length; index++)
            {
                if (vertexInputs[index].Location is null)
                {
                    return;
                }
            }

            throw new ArgumentException("An empty vertex layout requires at least one reflected system-value vertex input.", nameof(vertexInputs));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(vertexStride);

        var elementIndex = 0;

        for (var inputOffset = 0; inputOffset < vertexInputs.Length; inputOffset++)
        {
            var input = vertexInputs[inputOffset];

            if (input.Location is not int inputLocation)
            {
                continue;
            }

            var element = vertexElements[elementIndex++];

            if (element.Location != inputLocation)
            {
                throw new ArgumentException("Vertex elements must match reflected vertex input locations exactly.", nameof(vertexElements));
            }

            if (!Enum.IsDefined(element.Format))
            {
                throw new ArgumentOutOfRangeException(nameof(vertexElements));
            }

            if (VertexElementDescription.GetShaderValueType(element.Format) != input.ValueType)
            {
                throw new ArgumentException("Vertex element formats must match reflected vertex input value types.", nameof(vertexElements));
            }

            var elementEnd = checked(element.ByteOffset + VertexElementDescription.GetByteLength(element.Format));

            if (elementEnd > vertexStride)
            {
                throw new ArgumentException("Every vertex element must fit inside the declared vertex stride.", nameof(vertexStride));
            }

            if ((elementIndex > 1) && (element.Location == vertexElements[elementIndex - 2].Location))
            {
                throw new ArgumentException("Vertex element locations must be unique.", nameof(vertexElements));
            }
        }
    }
}
