// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal static class WorldPersistenceEcsTestHelper
{
    internal static int CaptureEcsRecord<TAction>(
        World world,
        in WorldPersistenceCaptureLease lease,
        ref TAction assertions)
        where TAction : struct, IAction<EbinRecordReadView>
    {
        Assert.That(
            WorldPersistenceEcsCaptureCoordinator.Capture(
                world,
                lease,
                out var record,
                out var reservation,
                out var diagnostic,
                CancellationToken.None),
            Is.EqualTo(EbinWriteStatus.Complete),
            diagnostic?.Category);
        Assert.That(diagnostic, Is.Null);
        var capturedRecord = record
            ?? throw new InvalidOperationException("The ECS capture coordinator did not produce its record.");
        using var reservationScope = reservation
            ?? throw new InvalidOperationException("The ECS capture coordinator did not retain its admission reservation.");
        Assert.That(
            EbinStructuralReader.Read(
                capturedRecord,
                WorldPersistenceEbinLimits.Create(world.Options),
                EbinReadPolicy.Canonical,
                ref assertions,
                out var bytesConsumed,
                out var readDiagnostic),
            Is.EqualTo(EbinReadStatus.Complete));
        Assert.That(readDiagnostic, Is.Null);
        Assert.That(bytesConsumed, Is.EqualTo(capturedRecord.Length));
        return capturedRecord.Length;
    }

    internal static byte[] CopyEcsRecord(World world, in WorldPersistenceCaptureLease lease)
    {
        var assertions = new EcsRecordCopyAction();
        var bytesWritten = CaptureEcsRecord(world, in lease, ref assertions);
        var record = assertions.Record
            ?? throw new InvalidOperationException("The capture helper did not return the actual ECS record.");

        if (record.Length != bytesWritten)
        {
            throw new InvalidOperationException("The captured ECS record length changed during copying.");
        }

        return record;
    }

    private struct EcsRecordCopyAction : IAction<EbinRecordReadView>
    {
        internal byte[]? Record { get; private set; }

        public void Invoke(scoped EbinRecordReadView record)
            => Record = record.EncodedBytes.ToArray();
    }
}
