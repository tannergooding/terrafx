// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Generators;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

[GenerateObjectGpuDataPacker]
internal readonly partial struct ObjectGpuData
{
    private readonly uint _metadata0;
    private readonly uint _metadata1;
    private readonly uint _metadata2;
    private readonly uint _metadata3;
    private readonly Vector4 _normalRow0;
    private readonly Vector4 _normalRow1;
    private readonly Vector4 _normalRow2;
    private readonly Matrix4x4 _previousWorld;
    private readonly Matrix4x4 _world;

    internal ObjectGpuData(
        Matrix4x4 world,
        Matrix4x4 previousWorld,
        Vector4 normalRow0,
        Vector4 normalRow1,
        Vector4 normalRow2,
        uint metadata0,
        uint metadata1,
        uint metadata2,
        uint metadata3)
    {
        _world = world;
        _previousWorld = previousWorld;
        _normalRow0 = normalRow0;
        _normalRow1 = normalRow1;
        _normalRow2 = normalRow2;
        _metadata0 = metadata0;
        _metadata1 = metadata1;
        _metadata2 = metadata2;
        _metadata3 = metadata3;
    }
}
