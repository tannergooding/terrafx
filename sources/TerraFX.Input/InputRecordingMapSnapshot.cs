// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Input;

internal sealed record InputRecordingMapSnapshot(
    InputActionMapV1 Map,
    ulong ActiveContextMask,
    ImmutableArray<long> ContextInsertionSequences);
