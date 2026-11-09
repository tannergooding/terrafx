// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Platform;

namespace TerraFX.Hosting;

internal readonly record struct PlatformInputCaptureRecord(
    long CaptureOrder,
    PlatformInputSample InputSample,
    PlatformTextInputSample TextInputSample,
    bool IsTextInput);
