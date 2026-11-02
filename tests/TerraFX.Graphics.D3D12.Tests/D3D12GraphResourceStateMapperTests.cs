// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.Versioning;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Interop.DirectX;

namespace TerraFX.Graphics.D3D12.Tests;

[SupportedOSPlatform("windows10.0.22000.0")]
[TestFixture]
internal sealed class D3D12GraphResourceStateMapperTests
{
    private static BufferDescription CreateBufferDescription(GpuMemoryKind memoryKind)
    {
        var usage = memoryKind switch {
            GpuMemoryKind.DeviceLocal => BufferUsage.Vertex
                | BufferUsage.Index
                | BufferUsage.Uniform
                | BufferUsage.Storage
                | BufferUsage.CopySource
                | BufferUsage.CopyDestination,
            GpuMemoryKind.Upload => BufferUsage.Vertex
                | BufferUsage.Index
                | BufferUsage.Uniform
                | BufferUsage.CopySource,
            GpuMemoryKind.Readback => BufferUsage.CopyDestination,
            _ => throw new ArgumentOutOfRangeException(nameof(memoryKind)),
        };
        return new BufferDescription(256, usage, memoryKind, structuredStride: null, string.Empty);
    }

    private static TextureDescription CreateTextureDescription(TextureFormat format)
    {
        var usage = (format == TextureFormat.D32Float)
            ? TextureUsage.DepthAttachment | TextureUsage.Sampled | TextureUsage.CopySource | TextureUsage.CopyDestination
            : TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.ColorAttachment | TextureUsage.CopySource | TextureUsage.CopyDestination;
        return new TextureDescription(
            TextureDimension.Dimension2D,
            4,
            4,
            1,
            1,
            1,
            format,
            usage,
            string.Empty);
    }

    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.Undefined, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON)]
    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.General, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON)]
    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.VertexRead, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER)]
    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.IndexRead, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_INDEX_BUFFER)]
    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.UniformRead, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER)]
    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.StorageRead, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS)]
    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.StorageWrite, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS)]
    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.CopySource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE)]
    [TestCase(GpuMemoryKind.DeviceLocal, GraphResourceState.CopyDestination, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST)]
    [TestCase(GpuMemoryKind.Upload, GraphResourceState.General, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ)]
    [TestCase(GpuMemoryKind.Upload, GraphResourceState.VertexRead, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ)]
    [TestCase(GpuMemoryKind.Upload, GraphResourceState.CopySource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_GENERIC_READ)]
    [TestCase(GpuMemoryKind.Readback, GraphResourceState.Undefined, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST)]
    [TestCase(GpuMemoryKind.Readback, GraphResourceState.CopyDestination, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST)]
    public void BufferGraphStatesMapToLegacyStates(
        GpuMemoryKind memoryKind,
        GraphResourceState state,
        D3D12_RESOURCE_STATES expectedState)
    {
        var description = CreateBufferDescription(memoryKind);

        Assert.That(D3D12GraphResourceStateMapper.GetBufferState(state, description), Is.EqualTo(expectedState));
    }

    [Test]
    public void BufferMapperRejectsTextureOnlyStates()
        => _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => D3D12GraphResourceStateMapper.GetBufferState(
                GraphResourceState.ShaderSampledRead,
                CreateBufferDescription(GpuMemoryKind.DeviceLocal)));

    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.Undefined, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON)]
    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.General, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COMMON)]
    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.CopySource, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_SOURCE)]
    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.CopyDestination, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST)]
    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.ShaderSampledRead, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE)]
    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.StorageRead, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS)]
    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.StorageWrite, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_UNORDERED_ACCESS)]
    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.ColorAttachment, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_RENDER_TARGET)]
    [TestCase(TextureFormat.Rgba16Float, GraphResourceState.Present, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT)]
    [TestCase(TextureFormat.D32Float, GraphResourceState.DepthRead, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_READ)]
    [TestCase(TextureFormat.D32Float, GraphResourceState.DepthWrite, D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_DEPTH_WRITE)]
    public void TextureGraphStatesMapToLegacyStates(
        TextureFormat format,
        GraphResourceState state,
        D3D12_RESOURCE_STATES expectedState)
    {
        var description = CreateTextureDescription(format);

        Assert.That(D3D12GraphResourceStateMapper.GetTextureState(state, description), Is.EqualTo(expectedState));
    }

    [Test]
    public void TextureMapperRejectsBufferOnlyStates()
        => _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => D3D12GraphResourceStateMapper.GetTextureState(
                GraphResourceState.VertexRead,
                CreateTextureDescription(TextureFormat.Rgba16Float)));
}
