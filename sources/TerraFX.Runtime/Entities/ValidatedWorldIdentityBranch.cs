// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal readonly record struct ValidatedWorldIdentityBranch(
    Guid BranchId,
    Guid CreationLineageId,
    Guid IssuanceNamespace,
    Guid SourceIdentityNamespace,
    Guid? ParentBranchId,
    long CreatedAtNextTick,
    uint FirstAssignmentOrdinal,
    byte Kind,
    ReadOnlyMemory<byte> SourceCheckpointDigest,
    ReadOnlyMemory<byte> ForkSourceRecord);
