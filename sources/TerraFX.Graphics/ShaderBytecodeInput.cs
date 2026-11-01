// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Buffers;

namespace TerraFX.Graphics;

/// <summary>Describes one caller-owned leased shader bytecode blob and its copied reflection metadata.</summary>
public sealed class ShaderBytecodeInput
{
    /// <summary>Initializes one nonempty shader bytecode input.</summary>
    /// <param name="stage">The shader stage.</param>
    /// <param name="format">The binary format.</param>
    /// <param name="bytecode">The caller-owned complete bytecode lease.</param>
    /// <param name="reflection">The immutable reflection record for the same stage.</param>
    /// <exception cref="ArgumentException">The bytecode is empty or the stage disagrees with reflection.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The stage or format is invalid.</exception>
    /// <exception cref="ObjectDisposedException">The bytecode lease has been disposed.</exception>
    public ShaderBytecodeInput(
        ShaderStage stage,
        ShaderBinaryFormat format,
        BufferReadLease<byte> bytecode,
        ShaderReflection reflection)
    {
        if ((stage == ShaderStage.None) || !Enum.IsDefined(stage))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        if ((format == ShaderBinaryFormat.None) || !Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        ArgumentNullException.ThrowIfNull(bytecode);
        ArgumentNullException.ThrowIfNull(reflection);

        if (bytecode.Length <= 0)
        {
            throw new ArgumentException("Shader bytecode leases must cover a nonempty binary blob.", nameof(bytecode));
        }

        if (reflection.Stage != stage)
        {
            throw new ArgumentException("The shader bytecode stage must match its reflection stage.", nameof(reflection));
        }

        Stage = stage;
        Format = format;
        Bytecode = bytecode;
        Reflection = reflection;
    }

    /// <summary>Gets the caller-owned bytecode lease.</summary>
    public BufferReadLease<byte> Bytecode { get; }

    /// <summary>Gets the bytecode format.</summary>
    public ShaderBinaryFormat Format { get; }

    /// <summary>Gets the immutable copied reflection metadata.</summary>
    public ShaderReflection Reflection { get; }

    /// <summary>Gets the shader stage.</summary>
    public ShaderStage Stage { get; }
}
