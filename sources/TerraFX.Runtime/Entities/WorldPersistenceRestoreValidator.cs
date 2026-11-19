// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceRestoreValidator
{
    internal static void Validate(
        ReadOnlyMemory<byte> sourceWorldRecord,
        ReadOnlyMemory<byte> targetWorldRecord,
        ValidatedWorldIdentityHistory sourceIdentityHistory,
        ValidatedWorldIdentityHistory targetIdentityHistory,
        WorldConstructionPlan constructionPlan,
        scoped ReadOnlySpan<byte> checkpointDigest,
        long nextTick)
    {
        ArgumentNullException.ThrowIfNull(sourceIdentityHistory);
        ArgumentNullException.ThrowIfNull(targetIdentityHistory);
        ArgumentNullException.ThrowIfNull(constructionPlan);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nextTick);

        if (checkpointDigest.Length != 32)
        {
            throw new ArgumentException("The source checkpoint digest must contain exactly 32 bytes.", nameof(checkpointDigest));
        }

        var sourceWorld = new EbinRecordReadView(sourceWorldRecord.Span);
        var targetWorld = new EbinRecordReadView(targetWorldRecord.Span);

        if ((sourceWorld.Header.SchemaId != targetWorld.Header.SchemaId)
            || (sourceWorld.Header.Major != targetWorld.Header.Major)
            || (sourceWorld.Header.Minor != targetWorld.Header.Minor)
            || (sourceWorld.FieldCount != targetWorld.FieldCount))
        {
            throw new InvalidOperationException("The fresh candidate World record does not match the validated target schema.");
        }

        var hasComparedIdentityHistory = false;

        for (var fieldIndex = 0; fieldIndex < sourceWorld.FieldCount; fieldIndex++)
        {
            var sourceField = sourceWorld.GetField(fieldIndex);
            var targetField = targetWorld.GetField(fieldIndex);

            if (sourceField.Tag != targetField.Tag
                || sourceField.Kind != targetField.Kind
                || sourceField.IsRequired != targetField.IsRequired
                || sourceField.Count != targetField.Count)
            {
                throw new InvalidOperationException("A fresh candidate World field does not match the validated target record.");
            }

            if (sourceField.Tag != 5)
            {
                if (!sourceField.Payload.SequenceEqual(targetField.Payload))
                {
                    throw new InvalidOperationException($"Fresh Count/Write changed validated World field {sourceField.Tag}.");
                }

                continue;
            }

            if (hasComparedIdentityHistory)
            {
                throw new InvalidOperationException("The validated World record contains duplicate identity-history fields.");
            }

            hasComparedIdentityHistory = true;
            ValidateEcsRecord(sourceField.Payload, targetField.Payload);
        }

        if (!hasComparedIdentityHistory)
        {
            throw new InvalidOperationException("The validated World record omitted its identity history.");
        }

        ValidateIdentityHistory(
            sourceIdentityHistory,
            targetIdentityHistory,
            constructionPlan,
            checkpointDigest,
            nextTick);
    }

    private static bool AreSameBranch(ValidatedWorldIdentityBranch left, ValidatedWorldIdentityBranch right)
        => (left.BranchId == right.BranchId)
            && (left.CreationLineageId == right.CreationLineageId)
            && (left.IssuanceNamespace == right.IssuanceNamespace)
            && (left.SourceIdentityNamespace == right.SourceIdentityNamespace)
            && (left.ParentBranchId == right.ParentBranchId)
            && (left.CreatedAtNextTick == right.CreatedAtNextTick)
            && (left.FirstAssignmentOrdinal == right.FirstAssignmentOrdinal)
            && (left.Kind == right.Kind)
            && left.SourceCheckpointDigest.Span.SequenceEqual(right.SourceCheckpointDigest.Span)
            && left.ForkSourceRecord.Span.SequenceEqual(right.ForkSourceRecord.Span);

    private static void ValidateEcsRecord(
        scoped ReadOnlySpan<byte> sourceEcsRecord,
        scoped ReadOnlySpan<byte> targetEcsRecord)
    {
        var sourceEcs = new EbinRecordReadView(sourceEcsRecord);
        var targetEcs = new EbinRecordReadView(targetEcsRecord);

        if ((sourceEcs.Header.SchemaId != targetEcs.Header.SchemaId)
            || (sourceEcs.Header.Major != targetEcs.Header.Major)
            || (sourceEcs.Header.Minor != targetEcs.Header.Minor)
            || (sourceEcs.FieldCount != targetEcs.FieldCount))
        {
            throw new InvalidOperationException("The fresh candidate ECS record does not match the validated target schema.");
        }

        var hasComparedIdentityHistory = false;

        for (var fieldIndex = 0; fieldIndex < sourceEcs.FieldCount; fieldIndex++)
        {
            var sourceField = sourceEcs.GetField(fieldIndex);
            var targetField = targetEcs.GetField(fieldIndex);

            if (sourceField.Tag != targetField.Tag
                || sourceField.Kind != targetField.Kind
                || sourceField.IsRequired != targetField.IsRequired
                || sourceField.Count != targetField.Count)
            {
                throw new InvalidOperationException("A fresh candidate ECS field does not match the validated target record.");
            }

            if (sourceField.Tag != 5)
            {
                if (!sourceField.Payload.SequenceEqual(targetField.Payload))
                {
                    throw new InvalidOperationException($"Fresh Count/Write changed validated ECS field {sourceField.Tag}.");
                }

                continue;
            }

            if (hasComparedIdentityHistory || (sourceField.Kind != 22) || !sourceField.IsRequired)
            {
                throw new InvalidOperationException("The validated ECS identity history has an invalid shape.");
            }

            hasComparedIdentityHistory = true;
        }

        if (!hasComparedIdentityHistory)
        {
            throw new InvalidOperationException("The validated ECS record omitted its identity history.");
        }
    }

    private static void ValidateIdentityHistory(
        ValidatedWorldIdentityHistory source,
        ValidatedWorldIdentityHistory target,
        WorldConstructionPlan constructionPlan,
        scoped ReadOnlySpan<byte> checkpointDigest,
        long nextTick)
    {
        if ((target.WorldGuid != source.WorldGuid)
            || (target.ReplayLineageId != source.ReplayLineageId)
            || (target.ActiveBranchId != constructionPlan.IdentityBranchId)
            || (target.IssuanceNamespace != constructionPlan.IdentityBranchId)
            || (target.NextAssignmentOrdinal != source.NextAssignmentOrdinal)
            || (target.LiveEntityCount != source.LiveEntityCount)
            || (target.LiveSceneInstanceCount != source.LiveSceneInstanceCount)
            || (target.AssignmentsByOrdinal.Length != source.AssignmentsByOrdinal.Length)
            || (target.Branches.Length != checked(source.Branches.Length + 1)))
        {
            throw new InvalidOperationException("The restored identity history does not match the admitted Branch transformation.");
        }

        for (var index = 0; index < source.AssignmentsByOrdinal.Length; index++)
        {
            var sourceAssignment = source.AssignmentsByOrdinal[index];
            var targetAssignment = target.AssignmentsByOrdinal[index];

            if ((targetAssignment.StableId != sourceAssignment.StableId)
                || (targetAssignment.Kind != sourceAssignment.Kind)
                || (targetAssignment.AssignmentOrdinal != sourceAssignment.AssignmentOrdinal)
                || (targetAssignment.BranchIndex != sourceAssignment.BranchIndex)
                || (targetAssignment.SourceIdentityNamespace != sourceAssignment.SourceIdentityNamespace)
                || (targetAssignment.Origin != sourceAssignment.Origin)
                || (targetAssignment.IsLive != sourceAssignment.IsLive))
            {
                throw new InvalidOperationException("The restored identity assignments do not preserve the validated source history.");
            }
        }

        for (var index = 0; index < source.Branches.Length; index++)
        {
            if (!AreSameBranch(source.Branches[index], target.Branches[index]))
            {
                throw new InvalidOperationException("The restored identity history changed a prior branch record.");
            }
        }

        var branch = target.Branches[^1];

        if ((branch.BranchId != constructionPlan.IdentityBranchId)
            || (branch.CreationLineageId != source.ReplayLineageId)
            || (branch.IssuanceNamespace != constructionPlan.IdentityBranchId)
            || (branch.SourceIdentityNamespace != constructionPlan.IdentityBranchId)
            || (branch.ParentBranchId != source.ActiveBranchId)
            || (branch.CreatedAtNextTick != nextTick)
            || (branch.FirstAssignmentOrdinal != checked((uint)source.NextAssignmentOrdinal))
            || (branch.Kind != 2)
            || !branch.SourceCheckpointDigest.Span.SequenceEqual(checkpointDigest)
            || !branch.ForkSourceRecord.IsEmpty)
        {
            throw new InvalidOperationException("The restored identity history did not append its exact source-bound Branch record.");
        }

        var sourceLineages = source.LineageMembership.LineageIds;
        var targetLineages = target.LineageMembership.LineageIds;

        if (!sourceLineages.SequenceEqual(targetLineages))
        {
            throw new InvalidOperationException("The compatible Branch restore changed retained replay-lineage membership.");
        }
    }
}
