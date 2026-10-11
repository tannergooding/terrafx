// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Resources;

namespace TerraFX.CodeAnalysis;

internal static class ThrowHelper
{
    internal static void ThrowIfNull<T>([NotNull] T? value, string parameterName)
        where T : class
    {
        if (value is null)
        {
            ThrowArgumentNullException(parameterName);
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal static void ThrowInvalidOperationException(string message)
        => throw new InvalidOperationException(message);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal static void ThrowMissingManifestResourceException(string message)
        => throw new MissingManifestResourceException(message);

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowArgumentNullException(string parameterName)
        => throw new ArgumentNullException(parameterName);
}
