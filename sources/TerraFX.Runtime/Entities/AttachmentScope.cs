// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;

namespace TerraFX.Entities;

/// <summary>Identifies the owner scope of an attachment.</summary>
[SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "The reviewed ownership scope uses only World=1 and Entity=2; zero is intentionally invalid.")]
public enum AttachmentScope
{
    /// <summary>The World is the attachment owner.</summary>
    World = 1,

    /// <summary>A live Entity is the attachment owner.</summary>
    Entity = 2,
}
