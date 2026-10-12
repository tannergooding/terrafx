// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly struct IdentityAssignment
{
    internal IdentityAssignment(
        IdentityKind kind,
        IdentityOrigin origin,
        uint locator,
        int branchIndex,
        Guid stableId = default,
        Guid issuanceNamespace = default)
    {
        Kind = kind;
        Origin = origin;
        Locator = locator;
        BranchIndex = branchIndex;
        StableId = stableId;
        IssuanceNamespace = issuanceNamespace;
    }

    internal int BranchIndex { get; }

    internal Guid IssuanceNamespace { get; }

    internal IdentityKind Kind { get; }

    internal uint Locator { get; }

    internal IdentityOrigin Origin { get; }

    internal Guid StableId { get; }
}
