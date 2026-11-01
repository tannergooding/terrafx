// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Contains immutable factory metadata for one explicitly selected graphics backend.</summary>
public sealed class GraphicsBackendRegistration
{
    private readonly IGraphicsBackendFactory _factory;

    internal GraphicsBackendRegistration(IGraphicsBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if ((factory.Backend == GraphicsBackend.None) || !Enum.IsDefined(factory.Backend))
        {
            throw new ArgumentOutOfRangeException(nameof(factory));
        }

        _factory = factory;
        Backend = factory.Backend;
    }

    /// <summary>Gets the backend implemented by this registration.</summary>
    public GraphicsBackend Backend { get; }

    internal IGraphicsBackendFactory Factory => _factory;
}
