// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Buffers;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;

namespace TerraFX.Graphics.D3D12.Consumer;

internal static class Program
{
    [SupportedOSPlatform("windows10.0.22000.0")]
    [SuppressMessage("Globalization", "CA1303", Justification = "The output is a machine-checked consumer witness marker.")]
    private static void Main()
    {
        var registration = D3D12GraphicsBackend.CreateRegistration();

        if (registration.Backend != GraphicsBackend.D3D12)
        {
            throw new InvalidOperationException("The selected backend registration did not preserve D3D12 identity.");
        }

        if (typeof(GraphicsDevice).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length != 0)
        {
            throw new InvalidOperationException("GraphicsDevice must not expose public owner construction.");
        }

        if (typeof(GpuBuffer).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length != 0)
        {
            throw new InvalidOperationException("GpuBuffer must not expose public resource construction.");
        }

        if (typeof(GpuTexture).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length != 0)
        {
            throw new InvalidOperationException("GpuTexture must not expose public resource construction.");
        }

        if (typeof(GpuSampler).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length != 0)
        {
            throw new InvalidOperationException("GpuSampler must not expose public resource construction.");
        }

        if (typeof(GpuPipeline).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length != 0)
        {
            throw new InvalidOperationException("GpuPipeline must not expose public resource construction.");
        }

        var createPipelineMethod = typeof(GraphicsDevice).GetMethod(
            nameof(GraphicsDevice.CreatePipelineAsync),
            [typeof(PipelineDescription), typeof(CancellationToken)]);

        if ((createPipelineMethod is null) || (createPipelineMethod.ReturnType != typeof(Task<GpuPipeline>)))
        {
            throw new InvalidOperationException("The public CreatePipelineAsync contract is unavailable to nonfriend consumers.");
        }

        var createSamplerMethod = typeof(GraphicsDevice).GetMethod(
            nameof(GraphicsDevice.CreateSamplerAsync),
            [typeof(SamplerDescription), typeof(CancellationToken)]);

        if ((createSamplerMethod is null) || (createSamplerMethod.ReturnType != typeof(Task<GpuSampler>)))
        {
            throw new InvalidOperationException("The public CreateSamplerAsync contract is unavailable to nonfriend consumers.");
        }

        var uploadSpanMethod = typeof(GraphicsDevice).GetMethod(
            nameof(GraphicsDevice.UploadBufferAsync),
            [typeof(GpuBuffer), typeof(long), typeof(ReadOnlySpan<byte>), typeof(CancellationToken)]);
        var uploadLeaseMethod = typeof(GraphicsDevice).GetMethod(
            nameof(GraphicsDevice.UploadBufferAsync),
            [typeof(GpuBuffer), typeof(long), typeof(BufferReadLease<byte>), typeof(nint), typeof(nint), typeof(CancellationToken)]);
        var readbackBufferMethod = typeof(GraphicsDevice).GetMethod(
            nameof(GraphicsDevice.ReadbackBufferAsync),
            [typeof(GpuBuffer), typeof(long), typeof(nint), typeof(CancellationToken)]);
        var uploadTextureMethod = typeof(GraphicsDevice).GetMethod(
            nameof(GraphicsDevice.UploadTextureAsync),
            [
                typeof(GpuTexture),
                typeof(TextureSubresource),
                typeof(BufferReadLease<byte>),
                typeof(nint),
                typeof(TextureDataLayout),
                typeof(CancellationToken),
            ]);
        var readbackTextureMethod = typeof(GraphicsDevice).GetMethod(
            nameof(GraphicsDevice.ReadbackTextureAsync),
            [typeof(GpuTexture), typeof(TextureSubresource), typeof(CancellationToken)]);

        if ((uploadSpanMethod?.ReturnType != typeof(Task<UploadReceipt>))
            || (uploadLeaseMethod?.ReturnType != typeof(Task<UploadReceipt>))
            || (readbackBufferMethod?.ReturnType != typeof(Task<OwnedBuffer<byte>>))
            || (uploadTextureMethod?.ReturnType != typeof(Task<UploadReceipt>))
            || (readbackTextureMethod?.ReturnType != typeof(Task<TextureReadback>)))
        {
            throw new InvalidOperationException("The public buffer and texture transfer contracts are unavailable to nonfriend consumers.");
        }

        if (typeof(GraphicsPipelineDescription).GetConstructor([
            typeof(ImmutableArray<ShaderBytecodeInput>),
            typeof(ImmutableArray<VertexElementDescription>),
            typeof(int),
            typeof(ImmutableArray<TextureFormat>),
            typeof(TextureFormat?),
            typeof(PrimitiveTopology),
            typeof(PipelineBlendMode),
            typeof(PipelineDepthMode),
            typeof(PipelineCullMode),
            typeof(bool),
            typeof(int),
        ]) is null)
        {
            throw new InvalidOperationException("The public GraphicsPipelineDescription contract is unavailable to nonfriend consumers.");
        }

        if (typeof(ComputePipelineDescription).GetConstructor([
            typeof(ShaderBytecodeInput),
            typeof(int),
        ]) is null)
        {
            throw new InvalidOperationException("The public ComputePipelineDescription contract is unavailable to nonfriend consumers.");
        }

        if (typeof(VertexElementDescription).GetConstructor([
            typeof(int),
            typeof(VertexElementFormat),
            typeof(int),
        ]) is null)
        {
            throw new InvalidOperationException("The public VertexElementDescription contract is unavailable to nonfriend consumers.");
        }

        VerifyShaderReflectionSurface();

        Console.WriteLine("D3D12 public registration consumer passed with unsafe disabled, public transfer/pipeline/sampler signatures, no public owner/resource construction, and no native device activation.");
    }

    private static void VerifyShaderReflectionSurface()
    {
        var packageDigest = new byte[32];
        packageDigest[0] = 0xA5;
        var compilerIdentity = new ShaderCompilerIdentity(
            "TerraFX.Graphics.D3D12.Consumer",
            "contract",
            "1.0.0",
            "win-x64",
            ImmutableCollectionsMarshal.AsImmutableArray(packageDigest));
        var frameDataField = new ShaderFieldLayout(
            "color",
            ShaderValueType.Float4,
            byteOffset: 0,
            byteLength: 16,
            arrayCount: 1,
            arrayStride: 0);
        var fields = new[] { frameDataField };
        var frameDataLayout = new ShaderRecordLayout(
            "FrameData",
            16,
            ImmutableCollectionsMarshal.AsImmutableArray(fields));
        var recordLayouts = new[] { frameDataLayout };
        var frameUniformBuffer = new ShaderResourceSignature(
            ShaderBindingGroup.Frame,
            0,
            ShaderResourceKind.UniformBuffer,
            1);
        var resources = new[] {
            new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.Sampler, 1),
            new ShaderResourceSignature(ShaderBindingGroup.Frame, 1, ShaderResourceKind.StorageImage, 1),
            new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.StorageBuffer, 1),
            new ShaderResourceSignature(ShaderBindingGroup.Frame, 0, ShaderResourceKind.SampledImage, 1),
            frameUniformBuffer,
        };
        var output = new ShaderIoSignature("SV_Target", 0, location: null, ShaderValueType.Float4, ShaderInterpolation.Smooth);
        var outputs = new[] { output };
        var capabilities = new[] {
            ShaderCapability.DescriptorIndexing,
            ShaderCapability.DoublePrecision,
            ShaderCapability.Float16,
            ShaderCapability.WaveOperations,
        };
        var reflection = new ShaderReflection(
            ShaderStage.Pixel,
            "PSMain",
            compilerIdentity,
            [],
            ImmutableCollectionsMarshal.AsImmutableArray(outputs),
            ImmutableCollectionsMarshal.AsImmutableArray(resources),
            ImmutableCollectionsMarshal.AsImmutableArray(recordLayouts),
            ImmutableCollectionsMarshal.AsImmutableArray(capabilities),
            bindingLayoutVersion: 1,
            groupSizeX: 0,
            groupSizeY: 0,
            groupSizeZ: 0);

        packageDigest[0] = 0x5A;
        fields[0] = default;
        recordLayouts[0] = new ShaderRecordLayout("OtherRecord", 16, [frameDataField]);
        resources[4] = default;
        outputs[0] = default;
        capabilities[0] = ShaderCapability.None;

        if ((reflection.CompilerIdentity != compilerIdentity)
            || (reflection.RecordLayouts.Length != 1)
            || !StringComparer.Ordinal.Equals(reflection.RecordLayouts[0].Name, "FrameData")
            || (reflection.RecordLayouts[0].Fields[0] != frameDataField)
            || (reflection.Resources[0] != frameUniformBuffer)
            || (reflection.Outputs[0] != output)
            || (reflection.RequiredCapabilities[0] != ShaderCapability.Float16)
            || (reflection.RequiredCapabilities[1] != ShaderCapability.DoublePrecision)
            || (reflection.RequiredCapabilities[2] != ShaderCapability.WaveOperations)
            || (reflection.RequiredCapabilities[3] != ShaderCapability.DescriptorIndexing)
            || (reflection.Resources.Length != 5)
            || (reflection.Resources[0].Kind != ShaderResourceKind.UniformBuffer)
            || (reflection.Resources[1].Kind != ShaderResourceKind.SampledImage)
            || (reflection.Resources[2].Kind != ShaderResourceKind.StorageBuffer)
            || (reflection.Resources[3].Kind != ShaderResourceKind.StorageImage)
            || (reflection.Resources[4].Kind != ShaderResourceKind.Sampler))
        {
            throw new InvalidOperationException("The public ShaderReflection value contract is unavailable or not canonical for nonfriend consumers.");
        }
    }
}
