// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Platform;

internal readonly record struct PlatformInputSampleBatch(
    ImmutableArray<PlatformInputSample> Samples,
    long DiscardedSampleCount,
    ImmutableArray<PlatformTextInputSample> TextSamples = default,
    ImmutableArray<PlatformTextInputOverflow> TextOverflows = default,
    bool AllTextInputSamplesOverflowed = false,
    long DiscardedTextSampleCount = 0)
{
    internal bool Overflowed => DiscardedSampleCount != 0;
}
