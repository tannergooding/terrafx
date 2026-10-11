// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace TerraFX;

internal sealed class FoundationProfileBinding
{
    private readonly byte[] _kernelDigest;
    private readonly byte[] _registryDigest;
    private readonly byte[] _settingsDigest;

    internal FoundationProfileBinding(SpatialProfileId id, SpatialSettings settings)
    {
        SpatialKernel.ValidateProfile(id, settings);

        if (id.Value == FoundationCatalogue.LegacyProfileId)
        {
            ThrowHelper.ThrowArgumentException("A legacy spatial identity cannot bind the M02 closure.", nameof(id));
        }

        Id = id;
        Settings = settings;
        _settingsDigest = ComputeSettingsDigest(settings);
        _registryDigest = FoundationCatalogue.RegistryDigest.ToArray();
        _kernelDigest = FoundationCatalogue.KernelDigest.ToArray();
    }

    internal SpatialProfileId Id { get; }

    internal SpatialSettings Settings { get; }

    internal uint CoordinateId { get; } = 1;
    internal uint GeometryId { get; } = 2;
    internal uint SpatialId { get; } = 2;
    internal ReadOnlySpan<byte> KernelDigest => _kernelDigest;
    internal ReadOnlySpan<byte> RegistryDigest => _registryDigest;
    internal ReadOnlySpan<byte> SettingsDigest => _settingsDigest;

    internal static byte[] ComputeSettingsDigest(SpatialSettings settings)
    {
        SpatialKernel.ValidateSettings(settings);
        Unsafe.SkipInit(out InlineBuffer32<byte> storage); // The domain and settings fields fill the preimage before hashing.
        Span<byte> preimage = storage;

        "TerraFX.SpatialSettings.v2\0"u8.CopyTo(preimage);
        BinaryPrimitives.WriteInt32LittleEndian(preimage[27..], settings.MetersPerUnitExponent);
        preimage[31] = checked((byte)settings.ChunkSizeExponent);
        return SHA256.HashData(preimage);
    }

    internal EbinRecord CreateRecord() => new EbinRecord(FoundationCatalogue.ProfileId, 2, 0, [
        EbinPrimitives.UInt32(1, CoordinateId), EbinPrimitives.UInt32(2, GeometryId), EbinPrimitives.UInt32(3, SpatialId),
        EbinPrimitives.UInt8(5, checked((byte)Settings.ChunkSizeExponent)), EbinPrimitives.Bytes(6, SettingsDigest),
        EbinPrimitives.Bytes(7, RegistryDigest), EbinPrimitives.Bytes(8, KernelDigest),
        EbinPrimitives.Guid(9, Id.Value), EbinPrimitives.Int32(10, Settings.MetersPerUnitExponent),
    ]);
}
