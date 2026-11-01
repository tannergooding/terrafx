// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform;

/// <summary>Contains requested window creation preferences.</summary>
public sealed class WindowOptions
{
    /// <summary>Initializes window creation preferences.</summary>
    /// <param name="title">The UTF-16 presentation title without embedded null characters.</param>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="title"/> contains an embedded null character.</exception>
    public WindowOptions(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (title.AsSpan().Contains('\0'))
        {
            throw new ArgumentException("A window title cannot contain an embedded null character.", nameof(title));
        }

        Title = title;
    }

    /// <summary>Gets the requested logical client extent in 1/96-inch units.</summary>
    public Vector2 ClientSize { get; init; } = new(1280, 720);

    /// <summary>Gets the minimum logical client extent in 1/96-inch units.</summary>
    public Vector2 MinimumClientSize { get; init; } = new(1, 1);

    /// <summary>Gets the requested presentation mode.</summary>
    public WindowMode Mode { get; init; } = WindowMode.Windowed;

    /// <summary>Gets the optional requested monitor, or null for platform-default placement.</summary>
    public MonitorHandle? Monitor { get; init; }

    /// <summary>Gets whether the user may resize the window.</summary>
    public bool Resizable { get; init; } = true;

    /// <summary>Gets the presentation title.</summary>
    public string Title { get; }

    /// <summary>Gets whether the window is initially visible.</summary>
    public bool Visible { get; init; } = true;

    private static bool IsValidClientSize(Vector2 size)
        => size.IsFinite().All()
            && (size.X > 0.0f)
            && (size.Y > 0.0f);

    internal void ValidateForCreation()
    {
        if (!IsValidClientSize(ClientSize))
        {
            throw new ArgumentOutOfRangeException(nameof(ClientSize));
        }

        if (!IsValidClientSize(MinimumClientSize))
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumClientSize));
        }

        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(Mode));
        }

        if (Monitor is { IsValid: false })
        {
            throw new ArgumentException("The requested monitor handle is invalid.", nameof(Monitor));
        }
    }
}
