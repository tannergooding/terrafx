// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Numerics;

namespace TerraFX.Scenes;

internal sealed class ScenePrefabRootPose
{
    internal ScenePrefabRootPose(
        SceneEntitySelector target,
        SceneAuthoredComponentRecord localTransform,
        Vector3 localTransformPosition,
        ImmutableArray<byte>? rootPositionPayload)
    {
        ArgumentNullException.ThrowIfNull(localTransform);

        if (rootPositionPayload is { } payload
            && (payload.IsDefault || (payload.Length != 24)))
        {
            throw new ArgumentException("The Prefab RootPose RootPosition payload must contain exactly 24 bytes.", nameof(rootPositionPayload));
        }

        Target = target;
        LocalTransform = localTransform;
        LocalTransformPosition = localTransformPosition;
        RootPositionPayload = rootPositionPayload;
    }

    internal SceneAuthoredComponentRecord LocalTransform { get; }

    internal Vector3 LocalTransformPosition { get; }

    internal ImmutableArray<byte>? RootPositionPayload { get; }

    internal SceneEntitySelector Target { get; }
}
