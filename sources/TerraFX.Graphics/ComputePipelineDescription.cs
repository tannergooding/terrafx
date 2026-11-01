// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Graphics;

/// <summary>Describes one compute pipeline and its reflected shader stage.</summary>
public sealed class ComputePipelineDescription : PipelineDescription
{
    /// <summary>Initializes a compute pipeline description.</summary>
    /// <param name="shader">The compute shader input.</param>
    /// <param name="bindingLayoutVersion">The positive binding-layout version.</param>
    /// <exception cref="ArgumentException">The shader is not a compute stage or uses another binding-layout version.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="shader"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The binding-layout version is not positive.</exception>
    public ComputePipelineDescription(ShaderBytecodeInput shader, int bindingLayoutVersion)
        : base(bindingLayoutVersion)
    {
        ArgumentNullException.ThrowIfNull(shader);

        if (shader.Stage != ShaderStage.Compute)
        {
            throw new ArgumentException("Compute pipelines require exactly one compute shader.", nameof(shader));
        }

        if (shader.Reflection.BindingLayoutVersion != bindingLayoutVersion)
        {
            throw new ArgumentException("Compute shader reflection must use the pipeline binding-layout version.", nameof(bindingLayoutVersion));
        }

        Shader = shader;
    }

    /// <summary>Gets the compute shader input.</summary>
    public ShaderBytecodeInput Shader { get; }
}
