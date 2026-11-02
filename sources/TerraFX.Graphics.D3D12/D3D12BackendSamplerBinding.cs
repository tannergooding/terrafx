// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Graphics;

namespace TerraFX.Graphics.D3D12;

internal readonly record struct D3D12BackendSamplerBinding(
    ShaderBindingGroup Group,
    int Binding,
    IGraphicsBackendSampler Sampler);
