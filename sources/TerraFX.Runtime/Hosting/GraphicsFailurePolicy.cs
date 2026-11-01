// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Hosting;

/// <summary>Defines how the host responds to a graphics-device failure.</summary>
public enum GraphicsFailurePolicy
{
    /// <summary>Stop the host after reporting the graphics failure.</summary>
    StopHost = 0,
}
