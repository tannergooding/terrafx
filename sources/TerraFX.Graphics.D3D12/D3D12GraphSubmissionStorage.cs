// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Graphics;
using TerraFX.Interop.DirectX;

namespace TerraFX.Graphics.D3D12;

internal sealed class D3D12GraphSubmissionStorage : IGraphicsBackendGraphSubmissionStorage
{
    internal D3D12GraphSubmissionStorage(DescriptorBindingArrays[] bindingArrays, int renderTargetViewCapacity)
    {
        BindingArrays = bindingArrays;
        DescriptorSets =
            new D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice.D3D12BackendDescriptorSet[bindingArrays.Length];

        for (var index = 0; index < bindingArrays.Length; index++)
        {
            DescriptorSets[index] = bindingArrays[index].DescriptorSet;
        }

        RenderTargetViews = new D3D12_CPU_DESCRIPTOR_HANDLE[renderTargetViewCapacity];
    }

    internal DescriptorBindingArrays[] BindingArrays { get; }

    internal D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice.D3D12BackendDescriptorSet[] DescriptorSets { get; }

    internal D3D12_CPU_DESCRIPTOR_HANDLE[] RenderTargetViews { get; }

    public void Dispose()
    {
        foreach (var bindingArray in BindingArrays)
        {
            bindingArray.ConstructionStorage.Dispose();
        }

        Array.Clear(BindingArrays);
        Array.Clear(DescriptorSets);
        Array.Clear(RenderTargetViews);
    }

    internal readonly struct DescriptorBindingArrays
    {
        internal readonly IDisposable ConstructionStorage;
        internal readonly D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice.D3D12BackendDescriptorSet DescriptorSet;
        internal readonly D3D12BackendSampledImageBinding[] SampledImages;
        internal readonly D3D12BackendSamplerBinding[] Samplers;
        internal readonly D3D12BackendStorageBufferBinding[] StorageBuffers;
        internal readonly D3D12BackendStorageImageBinding[] StorageImages;
        internal readonly D3D12BackendUniformBufferBinding[] UniformBuffers;

        internal DescriptorBindingArrays(
            IDisposable constructionStorage,
            D3D12GraphicsBackend.D3D12BackendFactory.D3D12BackendDevice.D3D12BackendDescriptorSet descriptorSet,
            int uniformBufferCount,
            int sampledImageCount,
            int storageBufferCount,
            int storageImageCount,
            int samplerCount)
        {
            ArgumentNullException.ThrowIfNull(constructionStorage);
            ConstructionStorage = constructionStorage;
            ArgumentNullException.ThrowIfNull(descriptorSet);
            DescriptorSet = descriptorSet;
            UniformBuffers = new D3D12BackendUniformBufferBinding[uniformBufferCount];
            SampledImages = new D3D12BackendSampledImageBinding[sampledImageCount];
            StorageBuffers = new D3D12BackendStorageBufferBinding[storageBufferCount];
            StorageImages = new D3D12BackendStorageImageBinding[storageImageCount];
            Samplers = new D3D12BackendSamplerBinding[samplerCount];
        }
    }
}
