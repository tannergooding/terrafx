// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Entities;

internal interface IWorldAttachmentRegistration
{
    WorldAttachmentFactoryDescriptor Descriptor { get; }

    AttachmentScope Scope { get; }

    Type ValueType { get; }

    void Release(object value, WorldAttachmentReleaseBinding binding);

    ImmutableArray<Diagnostic> Validate(World world, object value);

    void ValidateEntityReferences(World world, object value, Entity excludedEntity);
}
