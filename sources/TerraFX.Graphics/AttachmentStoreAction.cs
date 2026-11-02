// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Defines whether a graph attachment's written contents remain valid after the pass.</summary>
public enum AttachmentStoreAction
{
    /// <summary>Preserve the written range for later graph accesses.</summary>
    Store = 0,

    /// <summary>Discard the written range after the pass.</summary>
    Discard = 1,
}
