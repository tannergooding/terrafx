// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

internal sealed class FoundationRecipe(Guid id, string parameters)
{
    private readonly byte[] _parameters = Convert.FromHexString(parameters);
    internal Guid Id { get; } = id;
    internal ReadOnlySpan<byte> Parameters => _parameters;
}
