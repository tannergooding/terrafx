// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Numerics;

namespace TerraFX.Platform;

/// <summary>Contains requested window creation preferences.</summary>
public sealed class WindowOptions
{
    private Vector2 _clientSize = new(1280, 720);
    private Vector2 _minimumClientSize = new(1, 1);
    private WindowMode _mode = WindowMode.Windowed;
    private MonitorHandle? _monitor;
    private bool _resizable = true;
    private bool _visible = true;

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
    public Vector2 ClientSize
    {
        get
        {
            return _clientSize;
        }
        init
        {
            _clientSize = ValidateClientSize(value, nameof(ClientSize));
        }
    }

    /// <summary>Gets the minimum logical client extent in 1/96-inch units.</summary>
    public Vector2 MinimumClientSize
    {
        get
        {
            return _minimumClientSize;
        }
        init
        {
            _minimumClientSize = ValidateClientSize(value, nameof(MinimumClientSize));
        }
    }

    /// <summary>Gets the requested presentation mode.</summary>
    public WindowMode Mode
    {
        get
        {
            return _mode;
        }
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(Mode));
            }

            _mode = value;
        }
    }

    /// <summary>Gets the optional requested monitor, or null for platform-default placement.</summary>
    public MonitorHandle? Monitor
    {
        get
        {
            return _monitor;
        }
        init
        {
            if (value is { IsValid: false })
            {
                throw new ArgumentException("The requested monitor handle is invalid.", nameof(Monitor));
            }

            _monitor = value;
        }
    }

    /// <summary>Gets whether the user may resize the window.</summary>
    public bool Resizable
    {
        get
        {
            return _resizable;
        }
        init
        {
            _resizable = value;
        }
    }

    /// <summary>Gets the presentation title.</summary>
    public string Title { get; }

    /// <summary>Gets whether the window is initially visible.</summary>
    public bool Visible
    {
        get
        {
            return _visible;
        }
        init
        {
            _visible = value;
        }
    }

    private static Vector2 ValidateClientSize(Vector2 size, string parameterName)
    {
        if (!size.IsFinite().All()
            || (size.X <= 0.0f)
            || (size.Y <= 0.0f))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return size;
    }
}
