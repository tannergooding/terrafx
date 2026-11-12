// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal readonly record struct SkeletonJoint(
    Guid JointId,
    int ParentIndex,
    Vector3 BindTranslation,
    Quaternion BindRotation,
    Vector3 BindScale,
    Matrix4x4 InverseBind);
