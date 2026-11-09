// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

internal readonly record struct TextInputSessionSnapshot(
    long SessionId,
    TextInputSessionOptions Options)
{
    internal TerraFX.WindowHandle Window => Options.Window;
}
