// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Defines how graph compilation handles an imported resource's prior uses.</summary>
public enum GraphImportCompletionPolicy
{
    /// <summary>Carry the resource owner's actual last-use dependency into submission.</summary>
    WaitForLastUse = 0,

    /// <summary>Reject import while the resource owner has an incomplete use.</summary>
    RequireIdle = 1,
}
