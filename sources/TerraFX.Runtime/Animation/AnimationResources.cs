// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Animation;

/// <summary>Provides stable resource identities owned by Animation.</summary>
public static class AnimationResources
{
    private static readonly Guid s_rootMotionRequestsId =
        new ContentSchemaIdentityAssignment("resource/animation/root-motion/v1").SchemaId;

    /// <summary>Gets the fixed resource identity for copied root-motion requests.</summary>
    public static Guid RootMotionRequestsId => s_rootMotionRequestsId;
}
