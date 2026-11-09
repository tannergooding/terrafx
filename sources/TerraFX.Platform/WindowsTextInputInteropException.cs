// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Platform;

[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "This exception exists only to preserve a native TSF HRESULT from an internal call.")]
internal sealed class WindowsTextInputInteropException : InvalidOperationException
{
    internal WindowsTextInputInteropException(string operation, int hresult)
        : base($"Windows TSF {operation} failed with HRESULT 0x{unchecked((uint)hresult):X8}.")
    {
        HResult = hresult;
        Operation = operation;
    }

    internal string Operation { get; }
}
