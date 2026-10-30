// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Input;

/// <summary>Defines one keyboard control by physical HID usage or logical key name.</summary>
public sealed class KeyboardControl
{
    /// <summary>Initializes a layout-dependent logical keyboard control.</summary>
    /// <param name="logicalKeyName">The normalized logical key name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logicalKeyName"/> is null.</exception>
    public KeyboardControl(string logicalKeyName)
    {
        ArgumentNullException.ThrowIfNull(logicalKeyName);

        Kind = KeyboardControlKind.LogicalKey;
        LogicalKeyName = logicalKeyName;
    }

    /// <summary>Initializes a physical keyboard control.</summary>
    /// <param name="usagePage">The USB HID usage page.</param>
    /// <param name="usage">The USB HID usage.</param>
    public KeyboardControl(ushort usagePage, ushort usage)
    {
        Kind = KeyboardControlKind.KeyboardHid;
        UsagePage = usagePage;
        Usage = usage;
    }

    /// <summary>Gets the control representation.</summary>
    public KeyboardControlKind Kind { get; }

    /// <summary>Gets the logical key name, or null for a physical HID control.</summary>
    public string? LogicalKeyName { get; }

    /// <summary>Gets the HID usage, or zero for a logical key.</summary>
    public ushort Usage { get; }

    /// <summary>Gets the HID usage page, or zero for a logical key.</summary>
    public ushort UsagePage { get; }
}
