// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

[Flags]
internal enum PlatformTextInputCapabilities : byte
{
    None = 0,
    CommittedText = 1,
    Composition = 2,
}
