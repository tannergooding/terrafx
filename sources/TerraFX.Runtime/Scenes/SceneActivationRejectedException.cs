// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using TerraFX;

namespace TerraFX.Scenes;

[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "This internal exception is a bounded control-flow signal carrying an expected Scene activation refusal.")]
[SuppressMessage(
    "Design",
    "CA1064:Exceptions should be public",
    Justification = "This internal exception is caught within the Scene activation executor and never escapes its task boundary.")]
internal sealed class SceneActivationRejectedException : Exception
{
    internal SceneActivationRejectedException(ImmutableArray<Diagnostic> diagnostics)
        : base("Scene activation was rejected during bounded component restoration.")
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            throw new ArgumentException("A rejected Scene activation must retain diagnostics.", nameof(diagnostics));
        }

        Diagnostics = [.. diagnostics];
    }

    internal ImmutableArray<Diagnostic> Diagnostics { get; }
}
