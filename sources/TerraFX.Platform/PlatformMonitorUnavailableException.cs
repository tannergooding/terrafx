// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

internal sealed class PlatformMonitorUnavailableException : InvalidOperationException
{
    public PlatformMonitorUnavailableException()
    {
    }

    public PlatformMonitorUnavailableException(string message)
        : base(message)
    {
    }

    public PlatformMonitorUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
