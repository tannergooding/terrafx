// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Scenes;

internal sealed class SceneEntity
{
    internal SceneEntity(Guid authoredEntityId, ImmutableArray<SceneAuthoredComponentRecord> components)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(authoredEntityId, Guid.Empty);

        if (components.IsDefault)
        {
            throw new ArgumentException("The authored component array must be initialized.", nameof(components));
        }

        AuthoredEntityId = authoredEntityId;
        Components = components;
    }

    internal Guid AuthoredEntityId { get; }

    internal ImmutableArray<SceneAuthoredComponentRecord> Components { get; }
}
