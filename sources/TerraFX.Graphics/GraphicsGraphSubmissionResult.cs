// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

internal readonly record struct GraphicsGraphSubmissionResult(
    long SubmissionSequence,
    long CompletedFence,
    uint DeviceEpoch);
