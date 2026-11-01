// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Provides the common binding-layout version for a typed GPU pipeline description.</summary>
public abstract class PipelineDescription
{
    /// <summary>Initializes a pipeline description with its binding-layout version.</summary>
    /// <param name="bindingLayoutVersion">The positive binding-layout version.</param>
    /// <exception cref="ArgumentOutOfRangeException">The binding-layout version is not positive.</exception>
    internal PipelineDescription(int bindingLayoutVersion)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bindingLayoutVersion);
        BindingLayoutVersion = bindingLayoutVersion;
    }

    /// <summary>Gets the positive binding-layout version shared by the pipeline stages.</summary>
    public int BindingLayoutVersion { get; }
}
