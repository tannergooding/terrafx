// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

internal sealed class FoundationSchemaDescriptor(Guid id, uint major, ImmutableArray<FoundationFieldDescriptor> fields)
{
    internal Guid Id { get; } = id;
    internal uint Major { get; } = major;
    internal ImmutableArray<FoundationFieldDescriptor> Fields { get; } = fields;
}
