// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Describes the text purpose for one focused input session.</summary>
public enum TextInputPurpose
{
    /// <summary>General-purpose text.</summary>
    FreeForm,

    /// <summary>Password or other protected text.</summary>
    Password,

    /// <summary>Search query text.</summary>
    Search,

    /// <summary>Email address text.</summary>
    Email,

    /// <summary>Uniform resource locator text.</summary>
    Url,

    /// <summary>Numeric text.</summary>
    Number,

    /// <summary>Telephone number text.</summary>
    Telephone
}
