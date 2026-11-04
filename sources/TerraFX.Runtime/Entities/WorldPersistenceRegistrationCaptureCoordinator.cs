// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.CompilerServices;
using TerraFX;

namespace TerraFX.Entities;

internal static class WorldPersistenceRegistrationCaptureCoordinator
{
    internal static EbinWriteStatus Capture(
        World world,
        WorldPersistenceCaptureLease lease,
        out byte[]? record,
        out WorldPersistenceAdmissionReservation? reservation,
        out EbinStructuralDiagnostic? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(world);
        lease.ValidateCurrent();

        if (!lease.IsBoundTo(world))
        {
            throw new ArgumentException("The capture lease must belong to this exact World.", nameof(lease));
        }

        record = null;
        reservation = null;
        var limits = WorldPersistenceEbinLimits.Create(world.Options);
        var registration = world.Registration;
        var status = WorldPersistenceComponentRegistrationArrayWriter.Measure(
            registration,
            limits,
            out var componentWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WorldPersistenceTagRegistrationArrayWriter.Measure(
            registration,
            limits,
            out var tagWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = WorldPersistenceResourceRegistrationArrayWriter.Measure(
            registration,
            limits,
            out var resourceWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var componentSlices = new EbinPayloadSlice[componentWorkspace.RecordCount];
        var componentRecordStorage = new byte[componentWorkspace.RecordStorageBytes];
        var componentCandidateStorage = new byte[componentWorkspace.MaximumCandidateBytes];
        var componentsArrayPayload = new byte[componentWorkspace.ArrayPayloadBytes];
        status = WorldPersistenceComponentRegistrationArrayWriter.Write(
            registration,
            limits,
            componentWorkspace,
            componentSlices,
            componentRecordStorage,
            componentCandidateStorage,
            componentsArrayPayload,
            out _,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var tagSlices = new EbinPayloadSlice[tagWorkspace.RecordCount];
        var tagRecordStorage = new byte[tagWorkspace.RecordStorageBytes];
        var tagCandidateStorage = new byte[tagWorkspace.MaximumCandidateBytes];
        var tagsArrayPayload = new byte[tagWorkspace.ArrayPayloadBytes];
        status = WorldPersistenceTagRegistrationArrayWriter.Write(
            registration,
            limits,
            tagWorkspace,
            tagSlices,
            tagRecordStorage,
            tagCandidateStorage,
            tagsArrayPayload,
            out _,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var resourceSlices = new EbinPayloadSlice[resourceWorkspace.RecordCount];
        var resourceRecordStorage = new byte[resourceWorkspace.RecordStorageBytes];
        var resourceCandidateStorage = new byte[resourceWorkspace.MaximumCandidateBytes];
        var resourcesArrayPayload = new byte[resourceWorkspace.ArrayPayloadBytes];
        status = WorldPersistenceResourceRegistrationArrayWriter.Write(
            registration,
            limits,
            resourceWorkspace,
            resourceSlices,
            resourceRecordStorage,
            resourceCandidateStorage,
            resourcesArrayPayload,
            out _,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        status = MeasureSystemRows(
            registration,
            limits,
            out var systemWorkspaces,
            out var systemBuffersStorage,
            out var systemRecordStorageBytes,
            out var systemMaximumCandidateBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var systemRegistrationBuffers = systemBuffersStorage
            ?? throw new InvalidOperationException("The system-registration workspace was not created.");
        var systemRecordSlices = new EbinPayloadSlice[registration.Systems.Length];
        var systemRecordStorage = new byte[systemRecordStorageBytes];
        var systemCandidateStorage = new byte[systemMaximumCandidateBytes];
        status = WriteSystemRows(
            registration,
            limits,
            systemWorkspaces,
            systemRegistrationBuffers,
            systemRecordSlices,
            systemRecordStorage,
            systemCandidateStorage,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var systemOrderScratch = new int[registration.Systems.Length];
        var orderedSystemRecords = new EbinPayloadSlice[registration.Systems.Length];
        status = WorldPersistenceSystemRegistrationArrayWriter.Measure(
            registration,
            systemRecordSlices,
            systemRecordStorage,
            limits,
            systemOrderScratch,
            orderedSystemRecords,
            out var systemsArrayPayloadBytes,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var systemsArrayPayload = new byte[systemsArrayPayloadBytes];
        status = WorldPersistenceSystemRegistrationArrayWriter.Write(
            registration,
            systemRecordSlices,
            systemRecordStorage,
            limits,
            systemOrderScratch,
            orderedSystemRecords,
            systemsArrayPayload,
            out var systemsArrayBytesWritten,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        if (systemsArrayBytesWritten != systemsArrayPayload.Length)
        {
            throw new InvalidOperationException("The system-registration array extent changed after measurement.");
        }

        var rootPayloadStorage = new byte[checked(
            componentsArrayPayload.Length
            + tagsArrayPayload.Length
            + systemsArrayPayload.Length
            + resourcesArrayPayload.Length)];
        status = lease.MeasureRegistrationRecord(
            componentsArrayPayload,
            tagsArrayPayload,
            systemsArrayPayload,
            resourcesArrayPayload,
            rootPayloadStorage,
            out var rootWorkspace,
            out diagnostic);

        if (status is not EbinWriteStatus.Complete)
        {
            return status;
        }

        var systemArrayWorkspaceBytes = checked(
            ((long)registration.Systems.Length * Unsafe.SizeOf<EbinPayloadSlice>() * 2)
            + ((long)registration.Systems.Length * sizeof(int))
            + systemRecordStorage.Length
            + systemCandidateStorage.Length
            + systemsArrayPayload.Length);
        var workspaceBytes = checked(
            componentWorkspace.WorkspaceBytes
            + tagWorkspace.WorkspaceBytes
            + resourceWorkspace.WorkspaceBytes
            + systemArrayWorkspaceBytes
            + systemRegistrationBuffers.WorkspaceBytes
            + ((long)systemWorkspaces.Length * Unsafe.SizeOf<WorldPersistenceSystemRegistrationWorkspaceRequirements>())
            + rootWorkspace.WorkspaceBytes);

        var outputWorkspaceBytes = (long)rootWorkspace.RecordBytes;
        var scratchWorkspaceBytes = checked(workspaceBytes - outputWorkspaceBytes);

        if (!lease.TryReserveAdmissionBytes(scratchWorkspaceBytes, out var scratchReservation))
        {
            throw new InvalidOperationException("The registration capture workspace was refused by admission.");
        }

        var scratchReservationScope = scratchReservation
            ?? throw new InvalidOperationException("The registration scratch reservation was not created.");

        if (!lease.TryReserveAdmissionBytes(outputWorkspaceBytes, out var outputReservation))
        {
            scratchReservationScope.Dispose();
            throw new InvalidOperationException("The registration record reservation was refused by admission.");
        }

        var outputReservationScope = outputReservation
            ?? throw new InvalidOperationException("The registration record reservation was not created.");

        try
        {
            using (scratchReservationScope.AcquireUse(world, scratchWorkspaceBytes))
            using (outputReservationScope.AcquireUse(world, outputWorkspaceBytes))
            {
                status = WorldPersistenceComponentRegistrationArrayWriter.Write(
                    registration,
                    limits,
                    componentWorkspace,
                    componentSlices,
                    componentRecordStorage,
                    componentCandidateStorage,
                    componentsArrayPayload,
                    out _,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                status = WorldPersistenceTagRegistrationArrayWriter.Write(
                    registration,
                    limits,
                    tagWorkspace,
                    tagSlices,
                    tagRecordStorage,
                    tagCandidateStorage,
                    tagsArrayPayload,
                    out _,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                status = WorldPersistenceResourceRegistrationArrayWriter.Write(
                    registration,
                    limits,
                    resourceWorkspace,
                    resourceSlices,
                    resourceRecordStorage,
                    resourceCandidateStorage,
                    resourcesArrayPayload,
                    out _,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                status = WriteSystemRows(
                    registration,
                    limits,
                    systemWorkspaces,
                    systemRegistrationBuffers,
                    systemRecordSlices,
                    systemRecordStorage,
                    systemCandidateStorage,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                status = WorldPersistenceSystemRegistrationArrayWriter.Write(
                    registration,
                    systemRecordSlices,
                    systemRecordStorage,
                    limits,
                    systemOrderScratch,
                    orderedSystemRecords,
                    systemsArrayPayload,
                    out systemsArrayBytesWritten,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                if (systemsArrayBytesWritten != systemsArrayPayload.Length)
                {
                    throw new InvalidOperationException("The system-registration array extent changed after capture.");
                }

                var destination = new byte[rootWorkspace.RecordBytes];
                status = WorldPersistenceRegistrationWriter.Write(
                    registration,
                    componentsArrayPayload,
                    tagsArrayPayload,
                    systemsArrayPayload,
                    resourcesArrayPayload,
                    limits,
                    rootPayloadStorage,
                    new byte[rootWorkspace.RecordBytes],
                    destination,
                    out var bytesWritten,
                    out diagnostic);

                if (status is not EbinWriteStatus.Complete)
                {
                    return status;
                }

                if (bytesWritten != destination.Length)
                {
                    throw new InvalidOperationException("The registration record extent changed after capture measurement.");
                }

                record = destination;
            }

            reservation = outputReservationScope;
            return EbinWriteStatus.Complete;
        }
        finally
        {
            scratchReservationScope.Dispose();

            if (reservation is null)
            {
                outputReservationScope.Dispose();
            }
        }
    }

    private static EbinWriteStatus MeasureSystemRows(
        WorldRegistration registration,
        EbinReadLimits limits,
        out WorldPersistenceSystemRegistrationWorkspaceRequirements[] workspaces,
        out SystemRegistrationBufferStorage? buffersStorage,
        out int recordStorageBytes,
        out int maximumCandidateBytes,
        out EbinStructuralDiagnostic? diagnostic)
    {
        var systemCount = registration.Systems.Length;
        workspaces = new WorldPersistenceSystemRegistrationWorkspaceRequirements[systemCount];
        buffersStorage = null;
        recordStorageBytes = 0;
        maximumCandidateBytes = 0;
        var accessCount = 0;
        var accessStorageBytes = 0;
        var accessCandidateStorageBytes = 0;
        var accessArrayPayloadBytes = 0;
        var dependencyScratchCount = 0;
        var beforeArrayPayloadBytes = 0;
        var afterArrayPayloadBytes = 0;
        var schemaReferenceRecordBytes = 0;
        var rootPayloadStorageBytes = 0;
        diagnostic = null;

        for (var index = 0; index < systemCount; index++)
        {
            var system = registration.Systems[index];
            var status = WorldPersistenceSystemRegistrationWriter.MeasureWorkspace(
                system.Description,
                system.Factory.Descriptor,
                limits,
                out var workspace,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            workspaces[index] = workspace;
            accessCount = Math.Max(accessCount, workspace.AccessCount);
            accessStorageBytes = Math.Max(accessStorageBytes, workspace.AccessStorageBytes);
            accessCandidateStorageBytes = Math.Max(accessCandidateStorageBytes, workspace.AccessCandidateStorageBytes);
            accessArrayPayloadBytes = Math.Max(accessArrayPayloadBytes, workspace.AccessArrayPayloadBytes);
            dependencyScratchCount = Math.Max(dependencyScratchCount, workspace.DependencyScratchCount);
            beforeArrayPayloadBytes = Math.Max(beforeArrayPayloadBytes, workspace.BeforeArrayPayloadBytes);
            afterArrayPayloadBytes = Math.Max(afterArrayPayloadBytes, workspace.AfterArrayPayloadBytes);
            schemaReferenceRecordBytes = Math.Max(schemaReferenceRecordBytes, workspace.SchemaReferenceRecordBytes);
            rootPayloadStorageBytes = Math.Max(rootPayloadStorageBytes, workspace.RootPayloadStorageBytes);
        }

        var maximumWorkspace = new WorldPersistenceSystemRegistrationWorkspaceRequirements(
            accessCount,
            accessStorageBytes,
            accessCandidateStorageBytes,
            accessArrayPayloadBytes,
            dependencyScratchCount,
            beforeArrayPayloadBytes,
            afterArrayPayloadBytes,
            schemaReferenceRecordBytes,
            rootPayloadStorageBytes,
            recordBytes: 0);
        buffersStorage = new SystemRegistrationBufferStorage(maximumWorkspace);

        for (var index = 0; index < systemCount; index++)
        {
            var system = registration.Systems[index];
            var buffers = buffersStorage.Create(workspaces[index]);
            var status = WorldPersistenceSystemRegistrationWriter.Measure(
                system.Description,
                system.Factory.Descriptor,
                limits,
                ref buffers,
                out var measuredWorkspace,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            workspaces[index] = measuredWorkspace;
            recordStorageBytes = checked(recordStorageBytes + measuredWorkspace.RecordBytes);
            maximumCandidateBytes = Math.Max(maximumCandidateBytes, measuredWorkspace.RecordBytes);
        }

        return EbinWriteStatus.Complete;
    }

    private static EbinWriteStatus WriteSystemRows(
        WorldRegistration registration,
        EbinReadLimits limits,
        WorldPersistenceSystemRegistrationWorkspaceRequirements[] workspaces,
        SystemRegistrationBufferStorage buffersStorage,
        EbinPayloadSlice[] recordSlices,
        byte[] recordStorage,
        byte[] candidateStorage,
        out EbinStructuralDiagnostic? diagnostic)
    {
        diagnostic = null;
        var recordOffset = 0;

        for (var index = 0; index < registration.Systems.Length; index++)
        {
            var system = registration.Systems[index];
            var workspace = workspaces[index];
            var recordBytes = workspace.RecordBytes;
            var destination = recordStorage.AsSpan(recordOffset, recordBytes);
            var buffers = buffersStorage.Create(workspace);
            buffers.CandidateStorage = candidateStorage.AsSpan(0, recordBytes);
            buffers.Destination = destination;
            var status = WorldPersistenceSystemRegistrationWriter.Write(
                system.Description,
                system.Factory.Descriptor,
                limits,
                workspace,
                ref buffers,
                out var bytesWritten,
                out diagnostic);

            if (status is not EbinWriteStatus.Complete)
            {
                return status;
            }

            if (bytesWritten != recordBytes)
            {
                throw new InvalidOperationException("The system-registration record extent changed after measurement.");
            }

            recordSlices[index] = new EbinPayloadSlice(recordOffset, bytesWritten);
            recordOffset = checked(recordOffset + bytesWritten);
        }

        if (recordOffset != recordStorage.Length)
        {
            throw new InvalidOperationException("The system-registration record storage extent changed after measurement.");
        }

        return EbinWriteStatus.Complete;
    }

    private sealed class SystemRegistrationBufferStorage
    {
        private readonly byte[] _accessArrayPayloadStorage;
        private readonly byte[] _accessCandidateStorage;
        private readonly SystemResourceAccess[] _accessScratch;
        private readonly EbinPayloadSlice[] _accessSlices;
        private readonly byte[] _accessStorage;
        private readonly byte[] _afterArrayPayloadStorage;
        private readonly byte[] _beforeArrayPayloadStorage;
        private readonly Guid[] _dependencyScratch;
        private readonly byte[] _rootPayloadStorage;
        private readonly byte[] _schemaCandidateStorage;
        private readonly byte[] _schemaRecordStorage;

        internal SystemRegistrationBufferStorage(WorldPersistenceSystemRegistrationWorkspaceRequirements workspace)
        {
            _accessArrayPayloadStorage = new byte[workspace.AccessArrayPayloadBytes];
            _accessCandidateStorage = new byte[workspace.AccessCandidateStorageBytes];
            _accessSlices = new EbinPayloadSlice[workspace.AccessCount];
            _accessScratch = new SystemResourceAccess[workspace.AccessCount];
            _accessStorage = new byte[workspace.AccessStorageBytes];
            _afterArrayPayloadStorage = new byte[workspace.AfterArrayPayloadBytes];
            _beforeArrayPayloadStorage = new byte[workspace.BeforeArrayPayloadBytes];
            _dependencyScratch = new Guid[workspace.DependencyScratchCount];
            _rootPayloadStorage = new byte[workspace.RootPayloadStorageBytes];
            _schemaCandidateStorage = new byte[workspace.SchemaReferenceRecordBytes];
            _schemaRecordStorage = new byte[workspace.SchemaReferenceRecordBytes];
            WorkspaceBytes = GetWorkspaceBytes(workspace);
        }

        internal long WorkspaceBytes { get; }

        private static long GetWorkspaceBytes(WorldPersistenceSystemRegistrationWorkspaceRequirements workspace)
        {
            long bytes = 0;
            bytes = checked(bytes + ((long)workspace.AccessCount * Unsafe.SizeOf<SystemResourceAccess>()));
            bytes = checked(bytes + ((long)workspace.AccessCount * Unsafe.SizeOf<EbinPayloadSlice>()));
            bytes = checked(bytes + workspace.AccessStorageBytes);
            bytes = checked(bytes + workspace.AccessCandidateStorageBytes);
            bytes = checked(bytes + workspace.AccessArrayPayloadBytes);
            bytes = checked(bytes + workspace.AfterArrayPayloadBytes);
            bytes = checked(bytes + workspace.BeforeArrayPayloadBytes);
            bytes = checked(bytes + ((long)workspace.DependencyScratchCount * Unsafe.SizeOf<Guid>()));
            bytes = checked(bytes + workspace.RootPayloadStorageBytes);
            bytes = checked(bytes + (2L * workspace.SchemaReferenceRecordBytes));
            return bytes;
        }

        internal WorldPersistenceSystemRegistrationBuffers Create(
            WorldPersistenceSystemRegistrationWorkspaceRequirements workspace)
            => new() {
                AccessScratch = _accessScratch.AsSpan(0, workspace.AccessCount),
                AccessSlices = _accessSlices.AsSpan(0, workspace.AccessCount),
                AccessStorage = _accessStorage.AsSpan(0, workspace.AccessStorageBytes),
                AccessCandidateStorage = _accessCandidateStorage.AsSpan(0, workspace.AccessCandidateStorageBytes),
                AccessArrayPayloadStorage = _accessArrayPayloadStorage.AsSpan(0, workspace.AccessArrayPayloadBytes),
                AfterArrayPayloadStorage = _afterArrayPayloadStorage.AsSpan(0, workspace.AfterArrayPayloadBytes),
                BeforeArrayPayloadStorage = _beforeArrayPayloadStorage.AsSpan(0, workspace.BeforeArrayPayloadBytes),
                DependencyScratch = _dependencyScratch.AsSpan(0, workspace.DependencyScratchCount),
                RootPayloadStorage = _rootPayloadStorage.AsSpan(0, workspace.RootPayloadStorageBytes),
                SchemaCandidateStorage = _schemaCandidateStorage.AsSpan(0, workspace.SchemaReferenceRecordBytes),
                SchemaRecordStorage = _schemaRecordStorage.AsSpan(0, workspace.SchemaReferenceRecordBytes),
            };
    }
}
