// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Assets;

internal readonly struct AssetCodecRegistrationReceipt
{
    private readonly AssetStoreRegistration.IAssetCodec? _codec;
    private readonly object? _profileOrigin;

    internal AssetCodecRegistrationReceipt(
        AssetStoreRegistration.IAssetCodec codec,
        object profileOrigin)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(profileOrigin);
        _codec = codec;
        _profileOrigin = profileOrigin;
    }

    internal bool IsInitialized => _codec is not null;

    internal object? ProfileOrigin => _profileOrigin;

    internal AssetPackagePayloadCodec? RequiredPackageCodec => _codec?.RequiredPackageCodec;

    internal ContentSchemaReference Schema => _codec?.Schema ?? default;

    internal Type? ValueType => _codec?.ValueType;

    internal bool IsFor(AssetStoreRegistration.IAssetCodec codec)
        => ReferenceEquals(_codec, codec)
            && ReferenceEquals(_profileOrigin, codec.ProfileOrigin);

    internal bool IsSameCodec(AssetCodecRegistrationReceipt other)
        => ReferenceEquals(_codec, other._codec)
            && ReferenceEquals(_profileOrigin, other._profileOrigin);
}
