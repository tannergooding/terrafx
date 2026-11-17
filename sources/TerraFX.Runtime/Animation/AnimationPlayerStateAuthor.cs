// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX.Entities;

namespace TerraFX.Animation;

internal sealed class AnimationPlayerStateAuthor
{
    private readonly Guid _actorEntityId;
    private readonly ImmutableArray<AnimationTargetBindingAuthor> _bindings;
    private readonly WorldAssetReference _controller;
    private readonly long? _lastSampleTick;
    private readonly ImmutableArray<AnimationLayerStateAuthor> _layers;
    private readonly Guid? _motionActorId;
    private readonly long _nextPlaySequence;
    private readonly Guid _sceneInstanceId;
    private readonly float _speed;

    internal AnimationPlayerStateAuthor(
        Guid actorEntityId,
        Guid sceneInstanceId,
        WorldAssetReference controller,
        float speed,
        long nextPlaySequence,
        long? lastSampleTick,
        ImmutableArray<AnimationTargetBindingAuthor> bindings,
        ImmutableArray<AnimationLayerStateAuthor> layers,
        Guid? motionActorId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actorEntityId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sceneInstanceId, Guid.Empty);

        if (!controller.IsValid || (controller.SchemaMajor != 1))
        {
            throw new ArgumentException("An Animation player must reference a native/animation-controller major-1 asset.", nameof(controller));
        }

        if (!float.IsFinite(speed) || (speed < -4.0f) || (speed > 4.0f))
        {
            throw new ArgumentOutOfRangeException(nameof(speed), "Animation player speed must be finite and in [-4,4].");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextPlaySequence);

        if (lastSampleTick is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastSampleTick));
        }

        if (motionActorId == Guid.Empty)
        {
            throw new ArgumentException("A present MotionActorId cannot be empty.", nameof(motionActorId));
        }

        _bindings = AnimationRuntimeAuthorValidation.CopyRequired(bindings, 0, 4352, nameof(bindings));

        for (var index = 1; index < _bindings.Length; index++)
        {
            var previous = _bindings[index - 1];
            var current = _bindings[index];

            if ((previous.Domain > current.Domain)
                || ((previous.Domain == current.Domain)
                    && (TerraFX.FoundationCatalogue.CompareRfc(previous.SourceKey, current.SourceKey) >= 0)))
            {
                throw new ArgumentException("Animation target bindings must be uniquely ordered by domain and SourceKey.", nameof(bindings));
            }
        }

        _layers = AnimationRuntimeAuthorValidation.CopyRequired(layers, 1, 8, nameof(layers));

        for (var index = 0; index < _layers.Length; index++)
        {
            if (_layers[index].Index != index)
            {
                throw new ArgumentException("Animation layer indices must be contiguous and ordered from zero.", nameof(layers));
            }
        }

        _actorEntityId = actorEntityId;
        _sceneInstanceId = sceneInstanceId;
        _controller = controller;
        _speed = speed;
        _nextPlaySequence = nextPlaySequence;
        _lastSampleTick = lastSampleTick;
        _motionActorId = motionActorId;
    }

    internal Guid ActorEntityId => _actorEntityId;

    internal ImmutableArray<AnimationTargetBindingAuthor> Bindings => _bindings;

    internal WorldAssetReference Controller => _controller;

    internal long? LastSampleTick => _lastSampleTick;

    internal ImmutableArray<AnimationLayerStateAuthor> Layers => _layers;

    internal Guid? MotionActorId => _motionActorId;

    internal long NextPlaySequence => _nextPlaySequence;

    internal Guid SceneInstanceId => _sceneInstanceId;

    internal float Speed => _speed;
}
