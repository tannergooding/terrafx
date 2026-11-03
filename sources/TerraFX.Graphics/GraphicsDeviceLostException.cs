// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace TerraFX.Graphics;

[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "This exception is an internal backend-to-owner signal and is never part of the caller-facing failure contract.")]
[SuppressMessage(
    "Design",
    "CA1064:Exceptions should be public",
    Justification = "This exception is an internal backend-to-owner signal and is never part of the caller-facing failure contract.")]
internal sealed class GraphicsDeviceLostException : COMException
{
    internal GraphicsDeviceLostException(int nativeError)
        : base($"The native Graphics device was lost with native error 0x{nativeError:X8}.")
    {
        NativeError = nativeError;
    }

    internal int NativeError { get; }
}
