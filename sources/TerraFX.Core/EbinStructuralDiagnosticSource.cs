// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

/// <summary>Identifies the input phase that produced an EBIN structural diagnostic.</summary>
public enum EbinStructuralDiagnosticSource
{
    /// <summary>The field descriptions or requested construction are invalid.</summary>
    Construction = 0,

    /// <summary>Borrowed encoded payload bytes are invalid.</summary>
    EncodedInput = 1,

    /// <summary>The prepared candidate is invalid.</summary>
    Candidate = 2,
}
