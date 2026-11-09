// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;

namespace TerraFX.Platform;

internal readonly record struct PlatformTextInputOverflow(
    WindowHandle Window,
    long? SessionId,
    long DiscardedSampleCount);
