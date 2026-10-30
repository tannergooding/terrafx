// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Describes a validation failure in a typed input action map.</summary>
public sealed class ValidationIssue
{
    internal ValidationIssue(string code, string schemaPath)
    {
        Code = code;
        SchemaPath = schemaPath;
    }

    /// <summary>Gets the stable validation code.</summary>
    public string Code { get; }

    /// <summary>Gets the format-independent RFC 6901 schema path.</summary>
    public string SchemaPath { get; }
}
