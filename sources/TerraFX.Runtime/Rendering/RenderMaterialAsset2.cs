// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Rendering;

internal sealed class RenderMaterialAsset2
{
    internal RenderMaterialAsset2(
        RenderMaterialAssetData data,
        float pickAlphaCutoff,
        ImmutableArray<byte> encodedRecord)
    {
        if (!float.IsFinite(pickAlphaCutoff) || (pickAlphaCutoff < 0.0f) || (pickAlphaCutoff > 1.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(pickAlphaCutoff));
        }

        if (encodedRecord.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A Material 2.0 value requires its exact encoded record.", nameof(encodedRecord));
        }

        Data = data;
        PickAlphaCutoff = pickAlphaCutoff;
        EncodedRecord = encodedRecord;
    }

    internal RenderMaterialAssetData Data { get; }

    internal ImmutableArray<byte> EncodedRecord { get; }

    internal float PickAlphaCutoff { get; }
}
