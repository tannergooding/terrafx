// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;

namespace TerraFX.Hosting;

internal readonly record struct PlatformTextInputOverflowEvent(
    WindowHandle? Window,
    long HostTimestamp,
    long HostSequence,
    long? SessionId,
    long DiscardedSampleCount);
