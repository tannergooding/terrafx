// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Animation;

internal sealed class AnimationTargetBindingAuthor
{
    private readonly AnimationLocalPoseAuthor _baseline;
    private readonly byte _domain;
    private readonly Guid? _entityId;
    private readonly SpatialPosition3? _rootBaseline;
    private readonly Guid _sourceKey;

    internal AnimationTargetBindingAuthor(
        byte domain,
        Guid sourceKey,
        Guid? entityId,
        AnimationLocalPoseAuthor baseline,
        SpatialPosition3? rootBaseline)
    {
        if (domain > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(domain));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(sourceKey, Guid.Empty);
        ArgumentNullException.ThrowIfNull(baseline);

        if (domain == 0 != entityId.HasValue)
        {
            throw new ArgumentException("Animation EntityId is present exactly for Entity-domain bindings.", nameof(entityId));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("A present Animation EntityId cannot be empty.", nameof(entityId));
        }

        _domain = domain;
        _sourceKey = sourceKey;
        _entityId = entityId;
        _baseline = baseline;
        _rootBaseline = rootBaseline;
    }

    internal AnimationLocalPoseAuthor Baseline => _baseline;

    internal byte Domain => _domain;

    internal Guid? EntityId => _entityId;

    internal SpatialPosition3? RootBaseline => _rootBaseline;

    internal Guid SourceKey => _sourceKey;
}
