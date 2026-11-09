// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX;
using System.Collections.Immutable;

namespace TerraFX.Hosting;

internal readonly record struct PlatformTextInputEvent(
    WindowHandle Window,
    long HostTimestamp,
    long HostSequence,
    ImmutableArray<string> Commits);
