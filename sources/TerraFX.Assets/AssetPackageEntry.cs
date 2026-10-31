// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Assets;

internal readonly record struct AssetPackageEntry(
    AssetId AssetId,
    byte Codec,
    ulong PayloadOffset,
    ulong StoredLength,
    ulong DecodedLength);
