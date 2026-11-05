// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Hosting;

internal readonly record struct PlatformInputCaptureBatch(
    ImmutableArray<PhysicalInputEvent> Events,
    long DiscardedSampleCount,
    ImmutableArray<Diagnostic> Diagnostics)
{
    internal bool Overflowed => DiscardedSampleCount != 0;
}
