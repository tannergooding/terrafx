// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using TerraFX.Numerics;

namespace TerraFX.Animation;

internal sealed class AnimationLocalPoseAuthor
{
    private readonly Vector3 _position;
    private readonly Quaternion _rotation;
    private readonly Vector3 _scale;

    internal AnimationLocalPoseAuthor(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        AnimationRuntimeAuthorValidation.ValidateLocalPose(position, rotation, scale);
        _position = position;
        _rotation = rotation;
        _scale = scale;
    }

    internal Vector3 Position => _position;

    internal Quaternion Rotation => _rotation;

    internal Vector3 Scale => _scale;
}
