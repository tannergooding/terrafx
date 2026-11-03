// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX;
using TerraFX.Assets;

namespace TerraFX.Entities;

internal static class WorldPersistenceWorldRecordCaptureCoordinator
{
    private static readonly Guid s_schemaId = new ContentSchemaIdentityAssignment("state/world").SchemaId;

    internal static EbinWriteStatus Write(
        World world,
        WorldPersistenceCaptureLease lease,
        WorldPersistenceAdmissionReservation reservation,
        CompiledContentSchemaRegistry schemas,
        WorldPersistenceWorldRecordSources sources,
        WorldPersistenceWorldRecordCaptureWorkspaceRequirements workspace,
        scoped Span<uint> assetIdWorkspace,
        scoped Span<byte> rootPayloadStorage,
        scoped Span<byte> candidateStorage,
        scoped Span<byte> destination,
        out int assetIdCount,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(schemas);
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(world)
            || (sources.WorldGuid != lease.WorldGuid)
            || (sources.LeaseSequence != lease.Sequence))
        {
            throw new ArgumentException("The capture lease and World-root sources must belong to this exact World capture.", nameof(lease));
        }

        if (assetIdWorkspace.Length != workspace.AssetIdReferenceCount)
        {
            throw new ArgumentException("The AssetId workspace must match the exact live reference count.", nameof(assetIdWorkspace));
        }

        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var measuredReferenceCount = WorldPersistenceAssetIdSetWriter.MeasureReferenceCount(
            schemas,
            sources.EcsRecord,
            sources.ScenesRecord,
            limits);

        if (measuredReferenceCount != workspace.AssetIdReferenceCount)
        {
            return InvalidWorkspace(
                "asset-id-reference-count-mismatch",
                checked((ulong)measuredReferenceCount),
                checked((ulong)workspace.AssetIdReferenceCount),
                out assetIdCount,
                out bytesWritten,
                out diagnostic);
        }

        using (reservation.AcquireUse(world, workspace.WorkspaceBytes))
        {
        }

        using (reservation.AcquireUse(world, workspace.AssetIdWorkspaceBytes))
        {
            assetIdCount = WorldPersistenceAssetIdSetWriter.CopySortedUniqueAssetIds(
                schemas,
                sources.EcsRecord,
                sources.ScenesRecord,
                limits,
                assetIdWorkspace);
        }

        if (assetIdCount != workspace.AssetIdCount)
        {
            return InvalidWorkspace(
                "asset-id-count-mismatch",
                checked((ulong)assetIdCount),
                checked((ulong)workspace.AssetIdCount),
                out assetIdCount,
                out bytesWritten,
                out diagnostic);
        }

        if (sources.AssetIds.Length != assetIdCount)
        {
            return InvalidWorkspace(
                "asset-id-set-count-mismatch",
                checked((ulong)assetIdCount),
                checked((ulong)sources.AssetIds.Length),
                out assetIdCount,
                out bytesWritten,
                out diagnostic);
        }

        for (var index = 0; index < assetIdCount; index++)
        {
            if (assetIdWorkspace[index] != sources.AssetIds[index])
            {
                var actual = assetIdWorkspace[index];
                var expected = sources.AssetIds[index];
                diagnostic = CreateDiagnostic("asset-id-set-mismatch", actual, expected);
                bytesWritten = 0;
                return EbinWriteStatus.InvalidRecord;
            }
        }

        var status = WorldPersistenceWorldRecordWriter.Measure(
            sources,
            limits,
            rootPayloadStorage,
            out var measuredWorkspace,
            out diagnostic);
        bytesWritten = 0;

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var measuredCaptureWorkspace = new WorldPersistenceWorldRecordCaptureWorkspaceRequirements(
            measuredReferenceCount,
            assetIdCount,
            measuredWorkspace);

        if (!workspace.Matches(measuredCaptureWorkspace))
        {
            diagnostic = CreateDiagnostic(
                "world-workspace-mismatch",
                checked((ulong)measuredCaptureWorkspace.WorkspaceBytes),
                checked((ulong)workspace.WorkspaceBytes));
            return EbinWriteStatus.InvalidRecord;
        }

        return lease.WriteWorldRecord(
            reservation,
            sources,
            workspace.WorldRecord,
            rootPayloadStorage,
            candidateStorage,
            destination,
            out bytesWritten,
            out diagnostic);
    }

    private static EbinStructuralDiagnostic CreateDiagnostic(string category, ulong actual, ulong expected)
        => new(
            s_schemaId,
            7,
            category,
            null,
            actual,
            expected,
            EbinStructuralDiagnosticSource.Construction);

    private static EbinWriteStatus InvalidWorkspace(
        string category,
        ulong actual,
        ulong expected,
        out int assetIdCount,
        out int bytesWritten,
        out EbinStructuralDiagnostic? diagnostic)
    {
        assetIdCount = 0;
        bytesWritten = 0;
        diagnostic = CreateDiagnostic(category, actual, expected);
        return EbinWriteStatus.InvalidRecord;
    }
}
