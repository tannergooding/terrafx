// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Identifies the terminal state of an admitted GPU upload.</summary>
public enum UploadStatus
{
    /// <summary>The upload completed at its reported GPU fence.</summary>
    Ready = 0,

    /// <summary>The upload was rejected before native work began.</summary>
    Rejected = 1,
}
