// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

// The enclosing package tuple has no ProfileId and does not authorize a GUID alias.
internal sealed class FoundationProfileTuple
{
    private readonly byte[] _kernelDigest;
    private readonly byte[] _registryDigest;
    private readonly byte[] _settingsDigest;

    internal FoundationProfileTuple(uint coordinateId, uint geometryId, uint spatialId, SpatialSettings settings, scoped ReadOnlySpan<byte> settingsDigest, scoped ReadOnlySpan<byte> registryDigest, scoped ReadOnlySpan<byte> kernelDigest)
    {
        SpatialKernel.ValidateSettings(settings);

        if (settingsDigest.Length != 32 || registryDigest.Length != 32 || kernelDigest.Length != 32)
        {
            ThrowHelper.ThrowArgumentException("Every profile digest must contain exactly 32 bytes.");
        }

        CoordinateId = coordinateId;
        GeometryId = geometryId;
        SpatialId = spatialId;
        Settings = settings;
        _settingsDigest = settingsDigest.ToArray();
        _registryDigest = registryDigest.ToArray();
        _kernelDigest = kernelDigest.ToArray();
    }

    internal uint CoordinateId { get; }

    internal uint GeometryId { get; }

    internal SpatialSettings Settings { get; }

    internal uint SpatialId { get; }

    internal bool Matches(FoundationProfileBinding binding) =>
        CoordinateId == binding.CoordinateId && GeometryId == binding.GeometryId && SpatialId == binding.SpatialId && Settings == binding.Settings
        && _settingsDigest.AsSpan().SequenceEqual(binding.SettingsDigest) && _registryDigest.AsSpan().SequenceEqual(binding.RegistryDigest) && _kernelDigest.AsSpan().SequenceEqual(binding.KernelDigest);
}
