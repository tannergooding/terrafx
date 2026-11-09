// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

internal readonly record struct PlatformTextInputSample(
    WindowHandle Window,
    ImmutableArray<TextInputChange> Changes,
    long MonotonicTimestamp,
    long CaptureOrder = 0,
    bool GroupWithOtherCommits = false,
    long? SessionId = null,
    long? BaseDocumentRevision = null);
