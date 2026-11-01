// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Platform;

internal sealed class NativeHostAlreadyActiveException : InvalidOperationException
{
    internal const string Code = "NativeHostAlreadyActive";

    public NativeHostAlreadyActiveException()
        : base($"{Code}: only one native input-capture host may be active in the process.")
    {
    }

    public NativeHostAlreadyActiveException(string? message)
        : base(message)
    {
    }

    public NativeHostAlreadyActiveException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
