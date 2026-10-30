// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class DiagnosticTests
{
    private static Diagnostic CreateDiagnostic(
        string code = "sample",
        DiagnosticSeverity severity = DiagnosticSeverity.Warning,
        long monotonicTimestamp = 42,
        Guid? worldGuid = null,
        long? tick = 0,
        int? assetId = 1,
        ImmutableArray<DiagnosticContextEntry> context = default)
        => new(
            code,
            severity,
            monotonicTimestamp,
            1,
            worldGuid ?? new Guid("00112233-4455-4677-8899-aabbccddeeff"),
            2,
            3,
            4,
            5,
            6,
            tick,
            new Guid("10112233-4455-4677-8899-aabbccddeeff"),
            null,
            assetId,
            null,
            "SampleOperation",
            0,
            "Sample diagnostic.",
            context.IsDefault ? [new DiagnosticContextEntry("phase", "test")] : context);

    [Test]
    public void DiagnosticContextEntriesCompareOrdinalValues()
    {
        var entry = new DiagnosticContextEntry("Phase", "restore");
        var same = new DiagnosticContextEntry("Phase", "restore");
        var differentCase = new DiagnosticContextEntry("phase", "restore");

        Assert.That(entry == same, Is.True);
        Assert.That(entry.Equals(differentCase), Is.False);
        Assert.That(() => new DiagnosticContextEntry(string.Empty, "value"), Throws.ArgumentException);
        Assert.That(() => new DiagnosticContextEntry(null!, "value"), Throws.ArgumentNullException);
        Assert.That(() => new DiagnosticContextEntry("key", null!), Throws.ArgumentNullException);
    }

    [Test]
    public void DiagnosticQueriesEnforceTheSnapshotBounds()
    {
        var worldGuid = new Guid("00112233-4455-4677-8899-aabbccddeeff");
        var query = new DiagnosticQuery(8192, DiagnosticSeverity.Critical, worldGuid, 0);

        Assert.That(query.MaximumRecords, Is.EqualTo(8192));
        Assert.That(query.MinimumSeverity, Is.EqualTo(DiagnosticSeverity.Critical));
        Assert.That(query.WorldGuid, Is.EqualTo(worldGuid));
        Assert.That(query.MinimumTick, Is.Zero);
        Assert.That(new DiagnosticQuery(1, DiagnosticSeverity.Trace, null, null).MinimumTick, Is.Null);
        Assert.That(() => new DiagnosticQuery(0, DiagnosticSeverity.Trace, null, null), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new DiagnosticQuery(8193, DiagnosticSeverity.Trace, null, null), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new DiagnosticQuery(1, (DiagnosticSeverity)5, null, null), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new DiagnosticQuery(1, DiagnosticSeverity.Trace, Guid.Empty, null), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => new DiagnosticQuery(1, DiagnosticSeverity.Trace, null, -1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void DiagnosticRecordsCopyContextAndRetainTypedFacts()
    {
        ImmutableArray<DiagnosticContextEntry> context = [
            new DiagnosticContextEntry("active", "4"),
            new DiagnosticContextEntry("maximum", "4"),
        ];
        var diagnostic = CreateDiagnostic(context: context);

        Assert.That(diagnostic.Code, Is.EqualTo("sample"));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(diagnostic.MonotonicTimestamp, Is.EqualTo(42));
        Assert.That(diagnostic.HostId, Is.EqualTo(1));
        Assert.That(diagnostic.WorldGuid, Is.EqualTo(new Guid("00112233-4455-4677-8899-aabbccddeeff")));
        Assert.That(diagnostic.DeviceId, Is.EqualTo(2));
        Assert.That(diagnostic.SurfaceId, Is.EqualTo(3));
        Assert.That(diagnostic.DeviceEpoch, Is.EqualTo(4));
        Assert.That(diagnostic.SurfaceEpoch, Is.EqualTo(5));
        Assert.That(diagnostic.SwapchainEpoch, Is.EqualTo(6));
        Assert.That(diagnostic.Tick, Is.Zero);
        Assert.That(diagnostic.AssetId, Is.EqualTo(1));
        Assert.That(diagnostic.Operation, Is.EqualTo("SampleOperation"));
        Assert.That(diagnostic.NativeResult, Is.Zero);
        Assert.That(diagnostic.Message, Is.EqualTo("Sample diagnostic."));
        Assert.That(diagnostic.Context, Is.EqualTo(context));
    }

    [Test]
    public void DiagnosticRecordsRejectInvalidFields()
    {
        Assert.That(() => CreateDiagnostic(code: string.Empty), Throws.ArgumentException);
        Assert.That(() => CreateDiagnostic(severity: (DiagnosticSeverity)(-1)), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateDiagnostic(monotonicTimestamp: -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateDiagnostic(worldGuid: Guid.Empty), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateDiagnostic(tick: -1), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => CreateDiagnostic(assetId: 0), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
