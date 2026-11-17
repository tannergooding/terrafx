// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Animation;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Graphics;
using TerraFX.Hosting;
using TerraFX.Runtime.Tests.Entities;
using TerraFX.Runtime.Tests.Scenes;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

[TestFixture]
[NonParallelizable]
internal sealed partial class WorldPreparedRestoreSourceTests
{
    private static long AdmissionBytes(World world) => Field<long>(world, "_activePersistenceAdmissionBytes");

    private static EbinField ArrayField(uint tag, EbinReadLimits limits, params byte[][] records)
    {
        var storage = new byte[records.Sum(static record => record.Length)];
        var slices = new EbinPayloadSlice[records.Length];
        var offset = 0;

        for (var index = 0; index < records.Length; index++)
        {
            records[index].CopyTo(storage, offset);
            slices[index] = new EbinPayloadSlice(offset, records[index].Length);
            offset += records[index].Length;
        }

        Assert.That(EbinPayloadWriter.MeasureRecordArray(slices, storage, limits, out var bytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        var payload = new byte[bytes];
        Assert.That(EbinPayloadWriter.WriteRecordArray(slices, storage, limits, payload, out var written, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        Assert.That(written, Is.EqualTo(bytes));
        return new EbinField(tag, 24, 1, checked((ulong)records.Length), payload);
    }

    private static byte[] EncodeRecord(ReadOnlySpan<byte> original, uint tag, EbinField replacement, EbinReadLimits limits)
    {
        var root = new EbinRecordReadView(original);
        var fields = new EbinFieldSource[root.FieldCount];
        var payloads = new byte[root.FieldCount][];
        var bytes = 0;

        for (var index = 0; index < fields.Length; index++)
        {
            var field = root.GetField(index);
            var changed = field.Tag == tag;
            var payload = changed ? replacement.Payload.ToArray() : field.Payload.ToArray();
            payloads[index] = payload;
            fields[index] = new EbinFieldSource(
                field.Tag, changed ? replacement.Kind : field.Kind,
                changed ? replacement.Flags : field.IsRequired ? (ushort)1 : (ushort)0,
                changed ? replacement.Count : field.Count,
                new EbinPayloadSlice(bytes, payload.Length));
            bytes = checked(bytes + payload.Length);
        }

        var storage = new byte[bytes];
        var offset = 0;

        foreach (var payload in payloads)
        {
            payload.CopyTo(storage, offset);
            offset += payload.Length;
        }

        var source = new EbinRecordWriteView(
            root.Header.SchemaId, root.Header.Major, root.Header.Minor, fields, storage);
        Assert.That(EbinStructuralWriter.Measure(in source, limits, out bytes, out var diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        var result = new byte[bytes];
        Assert.That(EbinStructuralWriter.Write(
                in source, limits, new byte[bytes], result, out var written, out diagnostic),
            Is.EqualTo(EbinWriteStatus.Complete), diagnostic?.Category);
        Assert.That(written, Is.EqualTo(bytes));
        return result;
    }

    private static T Field<T>(object instance, string name)
        => (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Missing owner field {name}."));

    private static WorldPreparedSourceStorage.StorageUse ResourceStorage(WorldResourceSource<RootMotionResourceAuthor> source)
        => Field<WorldPreparedSourceStorage.StorageUse>(
            Field<WorldPreparedSourceAuthor<RootMotionResourceAuthor>>(source, "_author"), "_ownership");

    private static WorldLoadPreparedState State(WorldLoadPreparation preparation)
        => Field<WorldLoadPreparedState>(
            preparation.Plan ?? throw new InvalidOperationException("The real preparation omitted its plan."), "_state");

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationDuringTheRegisteredReadUnwindsWithoutCandidateOrEarlyRuntimeRelease(bool attachment)
    {
        using var fixture = new Fixture();
        fixture.Save();
        using var cancellation = new CancellationTokenSource();
        fixture.Counters.DuringRead = isAttachment => {
            if (isAttachment == attachment)
            {
                cancellation.Cancel();
            }
        };
        _ = Assert.Throws<OperationCanceledException>(() => fixture.Prepare(cancellationToken: cancellation.Token));
        Assert.Multiple(() => {
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceRestores, Is.Zero);
            Assert.That(fixture.Counters.AttachmentRestores, Is.Zero);
            Assert.That(fixture.Counters.AttachmentReleases, Is.Zero);
            Assert.That(AdmissionBytes(fixture.World), Is.Zero);
        });
        fixture.Counters.DuringRead = null;
        using var preparation = fixture.Prepare();
        Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
    }

    [TestCase("resource")]
    [TestCase("attachment")]
    [TestCase("acquisition")]
    public void CaughtProviderScopeMisuseIsStickyAndDoesNotContaminateTheNextLoad(string kind)
    {
        using var fixture = new Fixture();
        fixture.Save();
        fixture.Counters.Misuse = kind;
        var exception = Assert.Throws<InvalidOperationException>(() => fixture.Prepare());
        Assert.That(exception, Is.SameAs(fixture.Counters.CaughtFailure));
        Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
        Assert.That(AdmissionBytes(fixture.World), Is.Zero);
        fixture.Counters.Misuse = null;
        using var preparation = fixture.Prepare();
        Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
        var receipt = fixture.Commit(preparation);
        Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded));
        receipt.IdentityRemap?.Dispose();
    }

    [TestCase("resource")]
    [TestCase("attachment")]
    [TestCase("family")]
    [TestCase("attachment-family")]
    public void DuplicateDecodeAndWrongCapturedFamilyFaultOnlyTheirActualPreparedOwner(string kind)
    {
        using var fixture = new Fixture();
        fixture.Save();
        using var preparation = fixture.Prepare();
        var state = State(preparation);
        var schema = fixture.Attachment.Binding.Descriptor.Schema;

        if (kind == "resource")
        {
            _ = Assert.Throws<InvalidOperationException>(() => fixture.Registration.TryReadResourceSource<RootMotionResourceAuthor>(
                AnimationResources.RootMotionRequestsId, state, out _, out _, CancellationToken.None));
        }
        else if (kind == "attachment")
        {
            _ = Assert.Throws<InvalidOperationException>(() => fixture.Registration.TryReadWorldAttachmentSource<AnimationRuntimeAuthor>(
                schema, state, out _, out _, CancellationToken.None));
        }
        else if (kind == "family")
        {
            _ = Assert.Throws<InvalidOperationException>(() => fixture.Registration.TryReadResourceSource<int>(
                AnimationResources.RootMotionRequestsId, state, out _, out _, CancellationToken.None));
        }
        else
        {
            _ = Assert.Throws<InvalidOperationException>(() => fixture.Registration.TryReadWorldAttachmentSource<int>(
                schema, state, out _, out _, CancellationToken.None));
        }

        _ = Assert.Throws<InvalidOperationException>(() => fixture.Persistence.CommitLoadAsync(preparation.Plan!).GetAwaiter().GetResult());
        Assert.Multiple(() => {
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.AttachmentReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
        });
    }

    [Test]
    public void EntityScopedPlaybackCannotSubstituteForTheSelectedWorldOccurrence()
    {
        using var fixture = new Fixture(entityScoped: true);
        fixture.Save();
        using var preparation = fixture.Prepare(requirePrepared: false);
        Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Rejected));
        Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
        Assert.That(fixture.Counters.AttachmentReads, Is.Zero);
        Assert.That(fixture.Counters.AttachmentRestores, Is.Zero);
        Assert.That(AdmissionBytes(fixture.World), Is.Zero);
    }

    [Test]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The probe transfers into the real StorageUse by reflection; failed cleanup must retain that owner rather than dispose the probe again.")]
    public void FailedSourceCleanupRemainsStickyAndRetainsItsRealStorageCredit()
    {
        using var fixture = new Fixture();
        fixture.Save();
        var preparation = fixture.Prepare();
        var state = State(preparation);
        var resource = (WorldResourceSource<RootMotionResourceAuthor>)
            Field<ImmutableArray<IWorldPreparedResourceSource>>(state, "_preparedSources")[0];
        var storage = ResourceStorage(resource);
        var failure = new InvalidOperationException("The source use failed after consuming its actual release.");
        var probe = new SourceUseProbe(Field<IDisposable>(storage, "_sourceUse"), failure: failure);
        typeof(WorldPreparedSourceStorage.StorageUse).GetField("_sourceUse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(storage, probe);

        try
        {
            Assert.That(Assert.Throws<InvalidOperationException>(resource.Dispose), Is.SameAs(failure));
            Assert.That(Assert.Throws<InvalidOperationException>(resource.Dispose), Is.SameAs(failure));
            Assert.That(probe.DisposeCount, Is.EqualTo(1));
            Assert.That(resource.Author.Values, Has.Length.EqualTo(4096));
            Assert.That(AdmissionBytes(fixture.World), Is.GreaterThan(0));
        }
        finally
        {
            Assert.That(Assert.Throws<InvalidOperationException>(preparation.Dispose), Is.SameAs(failure));
        }

        Assert.That(Assert.Throws<InvalidOperationException>(storage.Dispose), Is.SameAs(failure));
        Assert.That(probe.DisposeCount, Is.EqualTo(1));
        Assert.That(AdmissionBytes(fixture.World), Is.GreaterThan(0));
        Assert.That(Field<long>(fixture.World, "_resourceOperationByteReservation"), Is.GreaterThan(0));
    }

    [Test]
    public void ForeignSourceCannotReplaceAnEqualLookingAuthorFromAnotherRealPreparation()
    {
        using var fixture = new Fixture();
        fixture.Save();
        using var first = fixture.Prepare();
        var firstState = State(first);
        var firstResources = Field<ImmutableArray<IWorldPreparedResourceSource>>(firstState, "_preparedSources");
        using var firstAttachment = ((WorldAttachmentSource<AnimationRuntimeAuthor>)
            Field<ImmutableArray<IWorldPreparedAttachmentSource>>(firstState, "_preparedAttachments")[0]).Retain();
        using var retained = ((WorldResourceSource<RootMotionResourceAuthor>)firstResources[0]).Retain();
        first.Dispose();
        using var second = fixture.Prepare();
        var secondState = State(second);
        var secondResources = Field<ImmutableArray<IWorldPreparedResourceSource>>(secondState, "_preparedSources");
        Assert.That(() => ((IWorldPreparedAttachmentSource)firstAttachment).GetAuthor(fixture.Registration.Attachments[0], secondState),
            Throws.InvalidOperationException);
        var foreignRegistration = new WorldAttachmentRegistration<AnimationRuntimeState, AnimationRuntimeAuthor>(
            AttachmentScope.World, fixture.Attachment.Binding);
        Assert.That(() => ((IWorldPreparedAttachmentSource)firstAttachment).GetAuthor(foreignRegistration, firstState), Throws.InvalidOperationException);
        secondResources[0].Dispose();
        typeof(WorldLoadPreparedState).GetField("_preparedSources", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(secondState, ImmutableArray.Create<IWorldPreparedResourceSource>(retained));
        _ = Assert.Throws<InvalidOperationException>(() => fixture.Persistence.CommitLoadAsync(second.Plan!).GetAwaiter().GetResult());
        Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(2));
        Assert.That(fixture.Counters.ResourceRestores, Is.Zero);
        Assert.That(fixture.Counters.AttachmentRestores, Is.Zero);
        Assert.That(fixture.World.NextTickIndex, Is.EqualTo(1));
    }

    [Test]
    public void LegacyCompositionDoesNotSelectAnimationFromCompiledSchemas()
    {
        using var fixture = new Fixture(registerResource: false, registerAttachment: false);
        fixture.Save();
        using var preparation = fixture.Prepare();
        var state = State(preparation);
        Assert.That(Field<ImmutableArray<IWorldPreparedResourceSource>>(state, "_preparedSources"), Is.Empty);
        Assert.That(Field<ImmutableArray<IWorldPreparedAttachmentSource>>(state, "_preparedAttachments"), Is.Empty);
        var receipt = fixture.Commit(preparation);
        Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded));
        Assert.That(fixture.Counters.Creates, Is.EqualTo(2));
        Assert.That(fixture.Counters.ResourceReads, Is.Zero);
        Assert.That(fixture.Counters.AttachmentReads, Is.Zero);
        receipt.IdentityRemap?.Dispose();
    }

    [TestCase(65_536, false)]
    [TestCase(1_048_576, true)]
    public void LoadLimitsAreSelectedByTheOperationRatherThanInferredFromWideSourceBytes(int maximumBytes, bool prepared)
    {
        using var fixture = new Fixture();
        fixture.Save();
        using var preparation = fixture.Persistence.PrepareLoadAsync(
            fixture.World, fixture.SavePath, new LoadOptions(WorldRestoreIntent.Branch) {
                IdentityBranchId = Guid.NewGuid(),
                MaximumSnapshotBytes = maximumBytes,
            }).GetAwaiter().GetResult();
        Assert.That(preparation.Status, Is.EqualTo(
            prepared ? WorldLoadPreparationStatus.Prepared : WorldLoadPreparationStatus.Rejected));
        Assert.That(fixture.Counters.Creates, Is.EqualTo(1));

        if (prepared)
        {
            Assert.That(State(preparation).Limits.MaximumRecordBytes, Is.EqualTo(maximumBytes));
            Assert.That(fixture.Counters.ResourceReadLimit, Is.EqualTo(maximumBytes));
            Assert.That(fixture.Counters.AttachmentReadLimit, Is.EqualTo(maximumBytes));
        }
        else
        {
            Assert.That(fixture.Counters.ResourceReads, Is.Zero);
            Assert.That(fixture.Counters.AttachmentReads, Is.Zero);
        }
    }

    [TestCase("resource-missing")]
    [TestCase("resource-duplicate")]
    [TestCase("attachment-missing")]
    [TestCase("attachment-duplicate")]
    [TestCase("resource-presence")]
    [TestCase("attachment-value")]
    public void MalformedOrMissingSelectedOccurrencesRejectBeforeCandidateConstruction(string mutation)
    {
        using var fixture = new Fixture();
        fixture.Save();
        var file = File.ReadAllBytes(fixture.SavePath);
        var limits = WorldPersistenceEbinLimits.Create(fixture.World.Options);
        var checkpoint = WorldPersistenceCheckpointWriter.ReadContents(file, limits, fixture.Registration);
        var world = checkpoint.WorldRecord.ToArray();
        var ecs = checkpoint.WorldContents.EcsRecord.ToArray();
        var root = new EbinRecordReadView(ecs);
        var resource = root.GetRecordArrayElement(3, 0).EncodedBytes.ToArray();
        var attachment = root.GetRecordArrayElement(4, 0).EncodedBytes.ToArray();

        switch (mutation)
        {
            case "resource-missing":
            {
                ecs = EncodeRecord(ecs, 3, ArrayField(3, limits), limits);
                break;
            }

            case "resource-duplicate":
            {
                ecs = EncodeRecord(ecs, 3, ArrayField(3, limits, resource, resource), limits);
                break;
            }

            case "attachment-missing":
            {
                ecs = EncodeRecord(ecs, 4, ArrayField(4, limits), limits);
                break;
            }

            case "attachment-duplicate":
            {
                ecs = EncodeRecord(ecs, 4, ArrayField(4, limits, attachment, attachment), limits);
                break;
            }

            case "resource-presence":
            {
                var payload = new EbinRecordReadView(resource).GetRecord(2).EncodedBytes.ToArray();
                var slots = new EbinRecordReadView(payload);
                var firstSlot = slots.GetRecordArrayElement(1, 0).EncodedBytes;
                var payloadOffset = payload.AsSpan().IndexOf(firstSlot);
                var first = firstSlot.ToArray();
                var booleanOffset = BinaryPrimitives.ReadUInt64LittleEndian(first.AsSpan(72));
                payload[checked(payloadOffset + (int)booleanOffset)] = 1;
                resource = EncodeRecord(resource, 2, new EbinField(2, 22, 1, 1, payload), limits);
                ecs = EncodeRecord(ecs, 3, ArrayField(3, limits, resource), limits);
                break;
            }

            case "attachment-value":
            {
                var payload = new EbinRecordReadView(attachment).GetRecord(3).EncodedBytes.ToArray();
                var offset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(72)));
                BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset), 0);
                attachment = EncodeRecord(attachment, 3, new EbinField(3, 22, 1, 1, payload), limits);
                ecs = EncodeRecord(ecs, 4, ArrayField(4, limits, attachment), limits);
                break;
            }

            default:
            {
                throw new InvalidOperationException("Unknown source mutation.");
            }
        }

        world = EncodeRecord(world, 5, new EbinField(5, 22, 1, 1, ecs), limits);
        File.WriteAllBytes(fixture.SavePath,
            WorldPersistenceCheckpointWriter.Create(checkpoint.HeaderRecord, world, 1, limits).FileRecord);
        using var preparation = fixture.Prepare(requirePrepared: false);
        Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Rejected));
        Assert.That(preparation.Diagnostics, Is.Not.Empty);
        Assert.Multiple(() => {
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceRestores, Is.Zero);
            Assert.That(fixture.Counters.AttachmentRestores, Is.Zero);
            Assert.That(AdmissionBytes(fixture.World), Is.Zero);
        });

        if (mutation == "resource-presence")
        {
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
            Assert.That(preparation.Diagnostics[0].Code, Is.EqualTo("WorldResourceRead.InvalidValue"));
        }
        else if (mutation == "attachment-value")
        {
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.AttachmentReads, Is.EqualTo(1));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MissingSelectedRegistrationIsAProtocolFailureRatherThanAnInferredProfile(bool resource)
    {
        using var fixture = new Fixture(registerResource: resource, registerAttachment: !resource);
        fixture.Save();
        _ = Assert.Throws<InvalidOperationException>(() => fixture.Prepare());
        Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
        Assert.That(fixture.Counters.ResourceReads, Is.Zero);
        Assert.That(fixture.Counters.AttachmentReads, Is.Zero);
        Assert.That(AdmissionBytes(fixture.World), Is.Zero);
    }

    [TestCase("native/mesh", 1)]
    [TestCase("native/mesh", 2)]
    [TestCase("native/material", 1)]
    [TestCase("native/material", 2)]
    [TestCase("native/animation-controller", 1)]
    [TestCase("native/clip", 1)]
    public void PrepareRejectsTheExactSelectedNonzeroMinorBeforeCandidateConstruction(string targetName, int targetMajor)
    {
        using var fixture = new Fixture(targetName: targetName, targetMajor: targetMajor);
        fixture.Save();

        using (var original = fixture.Prepare())
        {
            Assert.That(original.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
        }

        fixture.ReplaceReferencePackage(minor: 1);
        using var preparation = fixture.Prepare(requirePrepared: false);
        Assert.Multiple(() => {
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Rejected));
            Assert.That(preparation.Plan, Is.Null);
            Assert.That(preparation.Diagnostics, Has.Length.EqualTo(1));
            Assert.That(preparation.Diagnostics[0].Code, Is.EqualTo("load-package-asset-schema-mismatch"));
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(2));
            Assert.That(fixture.Counters.AttachmentReads, Is.EqualTo(2));
            Assert.That(fixture.Counters.ResourceRestores, Is.Zero);
            Assert.That(fixture.Counters.AttachmentRestores, Is.Zero);
            Assert.That(AdmissionBytes(fixture.World), Is.Zero);
        });

        var saved = fixture.Persistence.SaveAsync(
            fixture.World, new SaveOptions(fixture.SavePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
        Assert.That(saved.Status, Is.EqualTo(SaveStatus.Rejected));
        Assert.That(saved.Diagnostic?.Code, Is.EqualTo("save-package-asset-schema-mismatch"));
    }

    [Test]
    public void PreparedReferencePreflightUsesItsRetainedPackageWhenTheStoreChangesDuringSourceRead()
    {
        using var fixture = new Fixture(targetName: "native/mesh", targetMajor: 2);
        fixture.Save();
        fixture.Counters.DuringRead = attachment => {
            if (attachment)
            {
                fixture.ReplaceReferencePackage(minor: 1);
            }
        };
        using (var preparation = fixture.Prepare())
        {
            Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared));
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
        }

        fixture.Counters.DuringRead = null;
        using var next = fixture.Prepare(requirePrepared: false);
        Assert.That(next.Status, Is.EqualTo(WorldLoadPreparationStatus.Rejected));
        Assert.That(next.Diagnostics[0].Code, Is.EqualTo("load-package-asset-schema-mismatch"));
        Assert.That(AdmissionBytes(fixture.World), Is.Zero);
    }

    [TestCase("application/prepared-source-test/asset", 1)]
    [TestCase("native/mesh", 3)]
    [TestCase("native/material", 3)]
    [TestCase("native/animation-controller", 2)]
    [TestCase("native/clip", 2)]
    [TestCase("native/atlas", 1)]
    public void PreparedTargetsOutsideTheExactMinorPolicyKeepTheirRegisteredNonzeroMinor(string targetName, int targetMajor)
    {
        using var fixture = new Fixture(targetName: targetName, targetMajor: targetMajor);
        fixture.ReplaceReferencePackage(minor: 1);
        fixture.Save();
        using var preparation = fixture.Prepare();
        Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
        var receipt = fixture.Commit(preparation);
        Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded));
        Assert.That(fixture.World.TryGetAttachment<ReferenceValue>(out var restored), Is.True);
        Assert.That(restored!.Reference, Is.EqualTo(new WorldAssetReference(37, targetMajor)));
        receipt.IdentityRemap?.Dispose();
    }

    [Test]
    public void PreparedWorldWithoutM05KeepsTheLegacyMatcherForAnOtherwiseSelectedTarget()
    {
        using var fixture = new Fixture(
            registerResource: false, registerAttachment: false, targetName: "native/mesh", targetMajor: 2);
        fixture.ReplaceReferencePackage(minor: 1);
        fixture.Save();
        using var preparation = fixture.Prepare();
        var receipt = fixture.Commit(preparation);
        Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded));
        Assert.That(fixture.Counters.ResourceReads, Is.Zero);
        Assert.That(fixture.Counters.AttachmentReads, Is.Zero);
        receipt.IdentityRemap?.Dispose();
    }

    [Test]
    public void RetainedAuthorControlsConsumeAndReturnTheirAdmittedWorkspaceWithoutAnotherRead()
    {
        using var fixture = new Fixture();
        fixture.Save();
        using var preparation = fixture.Prepare();
        var source = (WorldResourceSource<RootMotionResourceAuthor>)
            Field<ImmutableArray<IWorldPreparedResourceSource>>(State(preparation), "_preparedSources")[0];
        var retained = new List<WorldResourceSource<RootMotionResourceAuthor>>();
        var refused = false;

        try
        {
            for (var index = 0; index < 256; index++)
            {
                try
                {
                    retained.Add(source.Retain());
                }
                catch (InvalidOperationException)
                {
                    refused = true;
                    break;
                }
            }

            Assert.That(refused, Is.True);
            Assert.That(retained, Is.Not.Empty);
            retained[^1].Dispose();
            retained.RemoveAt(retained.Count - 1);
            using var replacement = source.Retain();
            Assert.That(replacement.Author, Is.SameAs(source.Author));
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
        }
        finally
        {
            foreach (var retainedSource in retained)
            {
                retainedSource.Dispose();
            }
        }
    }

    [Test]
    public void RetainedSourceUsesOutlivePreparationClosureAndAuthorWrappersRevokeIndependently()
    {
        using var fixture = new Fixture();
        fixture.Save();
        var preparation = fixture.Prepare();
        var state = State(preparation);
        var resource = (WorldResourceSource<RootMotionResourceAuthor>)
            Field<ImmutableArray<IWorldPreparedResourceSource>>(state, "_preparedSources")[0];
        var attachment = (WorldAttachmentSource<AnimationRuntimeAuthor>)
            Field<ImmutableArray<IWorldPreparedAttachmentSource>>(state, "_preparedAttachments")[0];
        using var retainedResource = resource.Retain();
        using var retainedAttachment = attachment.Retain();
        var read = state.AcquireRead();
        preparation.Dispose();
        Assert.That(read.EcsRecord.Length, Is.GreaterThan(491_632));
        Assert.That(read.ScenesRecord.Length, Is.GreaterThan(0));
        Assert.That(AdmissionBytes(fixture.World), Is.GreaterThan(0));
        Assert.That(() => state.AcquireRead(), Throws.TypeOf<ObjectDisposedException>());
        read.Dispose();
        read.Dispose();
        Assert.That(() => resource.Author, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => resource.Retain(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => attachment.Author, Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => attachment.Retain(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(retainedResource.Author.Values, Has.Length.EqualTo(4096));
        Assert.That(retainedAttachment.Author.Players, Is.Empty);
        Assert.That(state.Checkpoint.WorldContents.Ecs.ResourceCount, Is.EqualTo(1));
        Assert.That(state.Registration, Is.SameAs(fixture.Registration));
        Assert.That(() => state.ConstructionPlan, Throws.InvalidOperationException);
        Assert.That(AdmissionBytes(fixture.World), Is.GreaterThan(0));
        retainedResource.Dispose();
        Assert.That(retainedAttachment.Author.NextCommandSequence, Is.EqualTo(1));
        retainedAttachment.Dispose();
        Assert.That(AdmissionBytes(fixture.World), Is.Zero);
        Assert.That(Field<long>(fixture.World, "_resourceOperationByteReservation"), Is.Zero);
        Assert.That(() => state.Checkpoint, Throws.InvalidOperationException);
        Assert.That(() => state.ConstructionPlan, Throws.InvalidOperationException);
        Assert.That(fixture.Counters.AttachmentReleases, Is.Zero);
        Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
    }

    [Test]
    public void RetiredCreationFactsResolveThroughTheActualSourceHistoryBeforeCandidateConstruction()
    {
        using var fixture = new Fixture();
        var entity = fixture.World.CreateEntity(new EntityDescription());
        var identity = fixture.World.GetEntityIdentity(entity);
        fixture.World.DestroyEntity(entity, HierarchyDestroyMode.DetachChildrenKeepWorld);
        _ = fixture.World.Step();
        fixture.Counters.ProbeIdentity = identity;
        fixture.Save();
        using var preparation = fixture.Prepare();
        Assert.That(fixture.Counters.ProbeResolved, Is.True);
        Assert.That(State(preparation).CompletedTick, Is.EqualTo(1));
        Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
        Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
    }

    [Test]
    public void SharedRuntimeAuthorStorageRemainsChargedWhenItsRegisteredReleaseIsQuarantined()
    {
        using var fixture = new Fixture();
        fixture.Save();
        using var preparation = fixture.Prepare();
        var originalWorld = fixture.World;
        var state = State(preparation);
        var receipt = fixture.Commit(preparation);
        preparation.Dispose();
        fixture.Counters.FailRestoredRelease = true;
        Assert.That(AdmissionBytes(originalWorld), Is.GreaterThan(0));
        Assert.That(Field<long>(originalWorld, "_resourceOperationByteReservation"), Is.GreaterThan(0));
        Assert.That(fixture.World.Dispose, Throws.Exception);
        Assert.That(fixture.Counters.RestoredReleases, Is.EqualTo(1));
        Assert.That(AdmissionBytes(originalWorld), Is.GreaterThan(0));
        Assert.That(Field<long>(originalWorld, "_resourceOperationByteReservation"), Is.GreaterThan(0));
        Assert.That(state.Checkpoint.WorldContents.Ecs.ResourceCount, Is.EqualTo(1));
        Assert.That(() => state.AcquireRead(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => File.Delete(Path.Combine(Path.GetDirectoryName(fixture.SavePath)!, "source.epkg")), Throws.TypeOf<IOException>());
        receipt.IdentityRemap?.Dispose();
    }

    [Test]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The probe transfers into the real StorageUse by reflection; both disposal tasks are joined before leaving its owner scope.")]
    public void SourceAcquisitionAndCacheTeardownRespectTheStorageLockOrder()
    {
        using var fixture = new Fixture();
        fixture.Save();
        using var preparation = fixture.Prepare();
        var state = State(preparation);
        var resource = (WorldResourceSource<RootMotionResourceAuthor>)
            Field<ImmutableArray<IWorldPreparedResourceSource>>(state, "_preparedSources")[0];
        var storage = ResourceStorage(resource);
        var sourceGate = Field<Lock>(Field<WorldRestoreSourceControl>(state, "_sourceControl"), "_gate");
        var storageGate = (Lock)typeof(WorldPreparedSourceStorage)
            .GetField("s_gate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        using var cleanupEntered = new ManualResetEventSlim();
        using var duplicateEntered = new ManualResetEventSlim();
        var probe = new SourceUseProbe(Field<IDisposable>(storage, "_sourceUse"), cleanupEntered);
        typeof(WorldPreparedSourceStorage.StorageUse).GetField("_sourceUse", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(storage, probe);
        var tasks = new List<Task>(2);
        var storageWasAvailable = false;
        var borrowedBytes = 0;
        sourceGate.Enter();

        try
        {
            tasks.Add(Task.Factory.StartNew(resource.Dispose,
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            Assert.That(cleanupEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            tasks.Add(Task.Factory.StartNew(() => {
                duplicateEntered.Set();
                storage.Dispose();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
            Assert.That(duplicateEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            // A timed probe makes the old inversion fail without leaving a deadlocked test host.
            storageWasAvailable = storageGate.TryEnter(TimeSpan.FromSeconds(1));

            if (storageWasAvailable)
            {
                storageGate.Exit();
                using var read = state.AcquireRead();
                borrowedBytes = read.EcsRecord.Length;
            }
        }
        finally
        {
            sourceGate.Exit();
            Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        Assert.That(storageWasAvailable, Is.True, "Cache teardown held the storage gate while waiting for the real source owner.");
        Assert.That(borrowedBytes, Is.GreaterThan(491_632));
        Assert.That(probe.DisposeCount, Is.EqualTo(1));
        storage.Dispose();
        resource.Dispose();
        preparation.Dispose();
        Assert.That(AdmissionBytes(fixture.World), Is.Zero);
        Assert.That(Field<long>(fixture.World, "_resourceOperationByteReservation"), Is.Zero);
        Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
    }

    [Test]
    public void StandardBranchReadsEachSelectedSourceBeforeConstructionAndRestoresTheSameAuthors()
    {
        using var fixture = new Fixture();
        fixture.Save();
        using var preparation = fixture.Prepare();
        var state = State(preparation);
        var resource = (WorldResourceSource<RootMotionResourceAuthor>)
            Field<ImmutableArray<IWorldPreparedResourceSource>>(state, "_preparedSources")[0];
        var attachment = (WorldAttachmentSource<AnimationRuntimeAuthor>)
            Field<ImmutableArray<IWorldPreparedAttachmentSource>>(state, "_preparedAttachments")[0];

        Assert.Multiple(() => {
            Assert.That(fixture.Counters.Creates, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.AttachmentReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceRestores, Is.Zero);
            Assert.That(fixture.Counters.AttachmentRestores, Is.Zero);
            Assert.That(resource.IsFrom(state), Is.True);
            Assert.That(attachment.IsFrom(state), Is.True);
            Assert.That(resource.Author.Values, Has.Length.EqualTo(4096));
            Assert.That(resource.Author.Values.All(static slot => !slot.Present), Is.True);
            Assert.That(attachment.Author.Players, Is.Empty);
            Assert.That(state.CompletedTick, Is.EqualTo(0));
            Assert.That(state.Limits.MaximumRecordBytes, Is.EqualTo(Fixture.SnapshotBytes));
        });

        var originalWorld = fixture.World;
        var receipt = fixture.Commit(preparation);
        preparation.Dispose();
        Assert.Multiple(() => {
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded));
            Assert.That(fixture.Counters.Creates, Is.EqualTo(2));
            Assert.That(fixture.Counters.ResourceReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.AttachmentReads, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceRestores, Is.EqualTo(1));
            Assert.That(fixture.Counters.AttachmentRestores, Is.EqualTo(1));
            Assert.That(fixture.Counters.ResourceRestoreAuthor, Is.SameAs(fixture.Counters.ResourceAuthor));
            Assert.That(fixture.Counters.AttachmentRestoreAuthor, Is.SameAs(fixture.Counters.AttachmentAuthor));
        });
        Assert.That(fixture.World.TryGetAttachment<AnimationRuntimeState>(out var restored), Is.True);
        Assert.That(restored!.Value, Is.SameAs(fixture.Counters.AttachmentAuthor));
        using var capture = fixture.World.AcquirePersistenceCaptureLease();
        Assert.That(capture.CountResourceStateRecord(0, CancellationToken.None), Is.EqualTo((nint)491_632));
        Assert.That(capture.CompletedTick, Is.EqualTo(0));
        Assert.That(() => fixture.Counters.ResourceReadBinding!.Complete(
            fixture.Counters.ResourceAuthor!, fixture.Counters.ResourceReadBinding.SourceByteLength), Throws.InvalidOperationException);
        Assert.That(() => fixture.Counters.AttachmentReadBinding!.Complete(
            fixture.Counters.AttachmentAuthor!, fixture.Counters.AttachmentReadBinding.SourceByteLength), Throws.InvalidOperationException);
        capture.Dispose();
        fixture.World.Dispose();
        Assert.That(fixture.Counters.RestoredReleases, Is.EqualTo(1));
        Assert.That(AdmissionBytes(originalWorld), Is.Zero);
        Assert.That(Field<long>(originalWorld, "_resourceOperationByteReservation"), Is.Zero);
        Assert.That(() => resource.Author, Throws.TypeOf<ObjectDisposedException>());
        receipt.IdentityRemap?.Dispose();
    }

    private readonly struct ConstructionCreate(Counters counters) : IWorldSystemCreate<ConstructionSystem>
    {
        public ConstructionSystem Invoke()
        {
            counters.Creates++;
            return new ConstructionSystem();
        }
    }

    private sealed class ConstructionFactory : IWorldSystemFactory
    {
        internal ConstructionFactory(Counters counters)
        {
            Binding = WorldSystemFactoryBinding.Stateless<ConstructionSystem, ConstructionCreate, ConstructionRelease>(
                new Guid("cbdff660-19e1-46ac-bdd5-6732cb7839ce"), 1,
                new WorldSystemSourceStateDeclaration(typeof(ConstructionSystem), []),
                new ConstructionCreate(counters), default);
        }

        public WorldSystemFactoryBinding Binding { get; }
    }

    private readonly struct ConstructionRelease : IWorldSystemRelease<ConstructionSystem>
    {
        public void Invoke(ConstructionSystem system) { }
    }

    private sealed class ConstructionSystem : IWorldSystem
    {
        public void Execute(WorldSystemContext context) { }
    }

    private sealed partial class Counters
    {
        internal AnimationRuntimeAuthor? AttachmentAuthor;
        internal WorldAttachmentReadBinding<AnimationRuntimeAuthor>? AttachmentReadBinding;
        internal int AttachmentReadLimit;
        internal int AttachmentReads;
        internal int AttachmentReleases;
        internal AnimationRuntimeAuthor? AttachmentRestoreAuthor;
        internal int AttachmentRestores;
        internal Exception? CaughtFailure;
        internal int Creates;
        internal Action<bool>? DuringRead;
        internal bool FailRestoredRelease;
        internal string? Misuse;
        internal WorldEntityIdentity? ProbeIdentity;
        internal bool ProbeResolved;
        internal RootMotionResourceAuthor? ResourceAuthor;
        internal WorldResourceReadBinding<RootMotionResourceAuthor>? ResourceReadBinding;
        internal int ResourceReadLimit;
        internal int ResourceReads;
        internal RootMotionResourceAuthor? ResourceRestoreAuthor;
        internal int ResourceRestores;
        internal int RestoredReleases;
        internal AnimationRuntimeState? RestoredState;
    }

    private sealed class CountingAttachment : IWorldAttachmentStateProvider<AnimationRuntimeState, AnimationRuntimeAuthor>
    {
        private readonly Counters _counters;
        private readonly AnimationRuntimeStateProvider _inner;

        internal CountingAttachment(CompiledContentSchemaRegistry schemas, Counters counters)
        {
            _counters = counters;
            _inner = new AnimationRuntimeStateProvider(schemas);
            var descriptor = _inner.Binding.Descriptor;
            Binding = WorldAttachmentFactoryBinding<AnimationRuntimeState, AnimationRuntimeAuthor>.CreateAggregate(
                descriptor.FactoryId, descriptor.FactoryVersion, schemas, descriptor.Schema,
                Field<WorldAttachmentRecordBinding<AnimationRuntimeState, AnimationRuntimeAuthor>>(_inner.Binding, "_aggregate"), this);
        }

        internal WorldAttachmentFactoryBinding<AnimationRuntimeState, AnimationRuntimeAuthor> Binding { get; }

        public nint Count(AnimationRuntimeState value, WorldAttachmentCaptureBinding binding, CancellationToken cancellationToken)
            => AnimationRuntimeStateCodec.Encode(value.Value, binding.Limits).Length;

        public WorldAttachmentReadResult<AnimationRuntimeAuthor> Read(
            scoped ReadOnlySpan<byte> source, WorldAttachmentReadBinding<AnimationRuntimeAuthor> binding, CancellationToken cancellationToken)
        {
            _counters.AttachmentReads++;
            Assert.That(_counters.Creates, Is.EqualTo(1));
            _counters.AttachmentReadBinding = binding;
            _counters.AttachmentReadLimit = binding.Limits.MaximumRecordBytes;
            _counters.DuringRead?.Invoke(true);
            var result = _inner.Read(source, binding, cancellationToken);
            _ = result.TryGetAuthorValue(out _counters.AttachmentAuthor);

            if (_counters.Misuse == "attachment")
            {
                try
                {
                    _ = binding.Complete(_counters.AttachmentAuthor!, source.Length);
                }
                catch (InvalidOperationException exception)
                {
                    _counters.CaughtFailure = exception;
                }
            }

            return result;
        }

        public void Release(AnimationRuntimeState value, WorldAttachmentReleaseBinding binding)
        {
            _counters.AttachmentReleases++;

            if (ReferenceEquals(value, _counters.RestoredState))
            {
                _counters.RestoredReleases++;

                if (_counters.FailRestoredRelease)
                {
                    throw new InvalidOperationException("The registered restored attachment release failed.");
                }
            }

            _inner.Release(value, binding);
        }

        public AnimationRuntimeState Restore(in AnimationRuntimeAuthor author, WorldAttachmentRestoreBinding binding, CancellationToken cancellationToken)
        {
            _counters.AttachmentRestores++;
            _counters.AttachmentRestoreAuthor = author;
            return _counters.RestoredState = _inner.Restore(in author, binding, cancellationToken);
        }

        public ImmutableArray<Diagnostic> Validate(
            AnimationRuntimeState value, WorldAttachmentValidationBinding binding, CancellationToken cancellationToken)
            => _inner.Validate(value, binding, cancellationToken);

        public nint Write(
            AnimationRuntimeState value, scoped Span<byte> destination,
            WorldAttachmentCaptureBinding binding, CancellationToken cancellationToken)
        {
            var encoded = AnimationRuntimeStateCodec.Encode(value.Value, binding.Limits);
            encoded.CopyTo(destination);
            return encoded.Length;
        }
    }

    private sealed class CountingResource : IWorldResourceFactory<RootMotionRequest>, IWorldResourceRecordCodec<RootMotionRequest, RootMotionResourceAuthor>
    {
        private readonly WorldResourceCodecBinding<RootMotionRequest> _binding;
        private readonly Counters _counters;
        private readonly RootMotionResourceProvider _inner;

        internal CountingResource(CompiledContentSchemaRegistry schemas, Counters counters)
        {
            _counters = counters;
            _inner = new RootMotionResourceProvider(schemas);
            _binding = WorldResourceCodecBinding<RootMotionRequest>.Create(_inner.Factory, this);
        }

        WorldResourceFactoryBinding<RootMotionRequest> IWorldResourceFactory<RootMotionRequest>.Binding => _inner.Factory.RegistrationBinding;
        WorldResourceCodecBinding<RootMotionRequest> IWorldResourceStateCodec<RootMotionRequest>.Binding => _binding;

        public nint Count(scoped WorldResourceStateView<RootMotionRequest> value, WorldResourceCaptureBinding binding, CancellationToken cancellationToken)
            => _inner.Count(value, binding, cancellationToken);

        public long GetOperationByteReservation(WorldResourceOperationKind operation) => _inner.GetOperationByteReservation(operation);

        public WorldResourceReadResult<RootMotionResourceAuthor> Read(
            scoped ReadOnlySpan<byte> source, WorldResourceReadBinding<RootMotionResourceAuthor> binding, CancellationToken cancellationToken)
        {
            _counters.ResourceReads++;
            Assert.That(_counters.Creates, Is.EqualTo(1));
            _counters.ResourceReadBinding = binding;
            _counters.ResourceReadLimit = binding.PersistenceLimits.MaximumRecordBytes;

            if (_counters.ProbeIdentity is { } probe)
            {
                Assert.That(binding.References.TryResolveHistoricalEntity(
                    probe.EntityId, probe.CreationLineageId, probe.CreationBranchId, out var resolved), Is.True);
                Assert.That(resolved, Is.EqualTo(probe));
                Assert.That(binding.References.TryResolveHistoricalEntity(
                    probe.EntityId, probe.CreationLineageId, Guid.NewGuid(), out _), Is.False);
                _counters.ProbeResolved = true;
            }

            _counters.DuringRead?.Invoke(false);
            var result = _inner.Read(source, binding, cancellationToken);
            _ = result.TryGetAuthorValue(out _counters.ResourceAuthor);

            try
            {
                if (_counters.Misuse == "resource")
                {
                    _ = binding.Complete(_counters.ResourceAuthor!, source.Length);
                }
                else if (_counters.Misuse == "acquisition")
                {
                    var anchors = (IEnumerable<WorldRestoreSourceControl>)typeof(WorldRestoreSourceControl)
                        .GetField("s_anchors", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                    using var read = anchors.Last().Owner.AcquireRead();
                }
            }
            catch (InvalidOperationException exception)
            {
                _counters.CaughtFailure = exception;
            }

            return result;
        }

        public void Restore(in RootMotionResourceAuthor author, WorldResourceRestoreBinding<RootMotionRequest> binding, CancellationToken cancellationToken)
        {
            _counters.ResourceRestores++;
            _counters.ResourceRestoreAuthor = author;
            _inner.Restore(in author, binding, cancellationToken);
        }

        public ImmutableArray<Diagnostic> Validate(
            scoped WorldResourceStateView<RootMotionRequest> value, WorldResourceValidationBinding binding, CancellationToken cancellationToken)
            => _inner.Validate(value, binding, cancellationToken);

        public ImmutableArray<Diagnostic> ValidateSourceReferences(
            in RootMotionResourceAuthor author, WorldResourceReferenceBinding binding, CancellationToken cancellationToken)
            => _inner.ValidateSourceReferences(in author, binding, cancellationToken);

        public nint Write(
            scoped WorldResourceStateView<RootMotionRequest> value, scoped Span<byte> destination,
            WorldResourceCaptureBinding binding, CancellationToken cancellationToken)
            => _inner.Write(value, destination, binding, cancellationToken);
    }

    private sealed partial class Fixture : IDisposable
    {
        internal const int SnapshotBytes = 32 * 1024 * 1024;
        private readonly AssetStoreRegistration _assetRegistration;
        private readonly AssetStore _assets;
        private readonly string _directory;
        private readonly ContentSchemaReference? _referenceSchema;
        internal readonly Counters Counters = new();
        internal readonly CountingAttachment Attachment;
        internal readonly WorldPersistence Persistence;
        internal readonly WorldRegistration Registration;
        internal readonly string SavePath;
        internal World World;

        internal Fixture(
            bool entityScoped = false,
            bool registerResource = true,
            bool registerAttachment = true,
            string? targetName = null,
            int targetMajor = 1,
            string? rootMotionCase = null)
        {
            _directory = Path.GetFullPath(Path.Combine("artifacts", "tst", "restore-sources", Guid.NewGuid().ToString("N")));
            _ = Directory.CreateDirectory(_directory);
            List<DataSchema> schemaDefinitions = [
                .. CoreContentSchemaCatalogue.PrimitiveSchemas,
                .. CoreContentSchemaCatalogue.FoundationSchemas,
                .. NativeAssetSchemaCatalogue.Schemas,
                .. SceneSchemaCatalogue.Schemas,
                .. RenderingSchemaCatalogue.Schemas,
                .. AnimationSchemaCatalogue.Schemas,
                .. AnimationSchemaCatalogue.PlaybackSchemas,
                .. StandaloneContinuationSchemaCatalogue.Schemas,
                WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                    "application/prepared-source-test/tag/v1", ContentSchemaRole.EntityTag, 0),
                WorldPersistenceWorldRecordWriterTests.CreateApplicationSchema(
                    "application/prepared-source-test/system-state/v1", ContentSchemaRole.SystemState, 7),
            ];
            DataSchema? referenceStateSchema = null;

            if (targetName is not null)
            {
                var targetAssignment = new ContentSchemaIdentityAssignment(targetName);
                var targetSchema = new ContentSchemaReference(targetAssignment.SchemaId, targetMajor, 0, ContentSchemaScope.Content);
                var existing = schemaDefinitions.FirstOrDefault(schema => schema.Schema == targetSchema);
                var fields = existing?.Fields ?? [];
                var owner = existing?.Owner ?? ContentSchemaOwner.Application;

                if (existing is null)
                {
                    schemaDefinitions.Add(new DataSchema(
                        targetSchema, targetAssignment, ContentSchemaRole.NativeAsset, owner,
                        ContentSchemaStorage.NotComponent, ContentTransientRestore.NotApplicable, fields));
                }

                schemaDefinitions.Add(new DataSchema(
                    new ContentSchemaReference(targetSchema.SchemaId, targetMajor, 1, ContentSchemaScope.Content),
                    targetAssignment, ContentSchemaRole.NativeAsset, owner,
                    ContentSchemaStorage.NotComponent, ContentTransientRestore.NotApplicable, fields));
                _referenceSchema = targetSchema;
                var stateAssignment = new ContentSchemaIdentityAssignment("application/prepared-source-test/asset-state/v1");
                referenceStateSchema = new DataSchema(
                    new ContentSchemaReference(stateAssignment.SchemaId, 1, 0, ContentSchemaScope.Content),
                    stateAssignment, ContentSchemaRole.AttachmentState, ContentSchemaOwner.Application,
                    ContentSchemaStorage.NotComponent, ContentTransientRestore.NotApplicable,
                    [new ContentFieldDescriptor(
                        1, 22, 0, true, 1, 1,
                        [new ContentSchemaReference(
                            new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId, 1, 0, ContentSchemaScope.Content)],
                        0, ContentUnit.None, ContentReferenceKind.Asset, ContentAuthoring.None, null)]);
                schemaDefinitions.Add(referenceStateSchema);
            }

            var schemas = new CompiledContentSchemaRegistry([.. schemaDefinitions]);
            var package = rootMotionCase is not null
                ? CreateRootMotionPackage(rootMotionCase)
                : _referenceSchema is { } referenceSchema
                    ? CreateReferencePackage(referenceSchema)
                    : SceneAssetStoreTests.Package.CreatePackage();
            var packagePath = Path.Combine(_directory, "source.epkg");
            File.WriteAllBytes(packagePath, package);
            var assetBuilder = new AssetStoreRegistrationBuilder(schemas);

            if (rootMotionCase is not null)
            {
                RegisterRootMotionAssetCodecs(assetBuilder, rootMotionCase);
            }

            if (_referenceSchema is { } selectedReferenceSchema)
            {
                _ = assetBuilder.RegisterCodec(new ReferenceAssetCodec<byte>(selectedReferenceSchema));
                _ = assetBuilder.RegisterCodec(new ReferenceAssetCodec<int>(new ContentSchemaReference(
                    selectedReferenceSchema.SchemaId, selectedReferenceSchema.Major, 1, ContentSchemaScope.Content)));
            }

            _assetRegistration = assetBuilder.Build();
            _assets = AssetStore.CreateAsync(new AssetStoreOptions(
                packagePath, SHA256.HashData(package), _assetRegistration)).GetAwaiter().GetResult();
            var resource = new CountingResource(schemas, Counters);
            Attachment = new CountingAttachment(schemas, Counters);
            var builder = new WorldRegistrationBuilder(schemas);

            if (registerResource)
            {
                _ = builder.RegisterResource(
                    new SystemResourceDescription(AnimationResources.RootMotionRequestsId, 4096, allowParallelAccess: false),
                    resource, resource);
            }

            if (registerAttachment)
            {
                _ = builder.RegisterAttachment(AttachmentScope.World, Attachment.Binding);
            }

            if (entityScoped)
            {
                _ = builder.RegisterAttachment(AttachmentScope.Entity, Attachment.Binding);
            }

            if (referenceStateSchema is not null)
            {
                _ = builder.RegisterAttachment(AttachmentScope.World, new ReferenceAttachmentProvider(
                    schemas, referenceStateSchema, _referenceSchema
                        ?? throw new InvalidOperationException("The reference attachment omitted its selected target schema.")).Binding);
            }

            _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
            _ = builder.RegisterSystem(new SystemDescription(
                new Guid("43293741-9732-4c44-977d-3e45cd811ffa"), 0, SimulationStage.Input, [], [], [], SystemThreadMode.OwnerThread),
                new ConstructionFactory(Counters));
            Registration = builder.Build();
            World = World.Create(new WorldOptions(Registration) {
                CpuWorkerCount = 0,
                InitialReplayLineageId = Guid.NewGuid(),
                MaximumSnapshotBytes = SnapshotBytes,
                WorldGuid = Guid.NewGuid(),
            });
            var value = new AnimationRuntimeState(new AnimationRuntimeAuthor(1, []));

            if (entityScoped)
            {
                World.SetAttachment(World.CreateEntity(new EntityDescription()), value, AttachmentOwnership.Transferred);
            }
            else if (registerAttachment)
            {
                World.SetAttachment(value, AttachmentOwnership.Transferred);
            }

            if (_referenceSchema is not null)
            {
                World.SetAttachment(new ReferenceValue(new WorldAssetReference(37, targetMajor)), AttachmentOwnership.Transferred);
            }

            if (rootMotionCase is not null)
            {
                InstallHistoricalRootMotion(rootMotionCase);
            }

            _ = World.Step();
            SavePath = Path.Combine(_directory, "source.state");
            Persistence = new WorldPersistence(_assets, WorldPersistenceHeaderWriterTests.CreateMetadata());
        }

        private static byte[] CreateReferencePackage(ContentSchemaReference schema)
            => SceneAssetStoreTests.Package.CreatePackage(new SceneAssetStoreTests.Package.Entry(
                37, SceneAssetStoreTests.Package.CreateEnvelope(
                    37, EbinPrimitives.Encode(new EbinRecord(schema.SchemaId, checked((uint)schema.Major), checked((uint)schema.Minor), [])))));

        public void Dispose()
        {
            World.Dispose();
            Persistence.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _assets.DisposeAsync().AsTask().GetAwaiter().GetResult();

            if (!Counters.FailRestoredRelease)
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        internal WorldLoadReceipt Commit(WorldLoadPreparation preparation)
        {
            var receipt = Persistence.CommitLoadAsync(preparation.Plan
                ?? throw new InvalidOperationException("The real load omitted its plan.")).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.EqualTo(WorldLoadStatus.Loaded), receipt.Rejection?.Message);
            World = receipt.ReplacementWorld ?? throw new InvalidOperationException("The actual commit omitted its candidate.");
            receipt.OldWorldRetirement!.GetAwaiter().GetResult();
            return receipt;
        }

        internal WorldLoadPreparation Prepare(bool requirePrepared = true, CancellationToken cancellationToken = default)
        {
            var preparation = Persistence.PrepareLoadAsync(World, SavePath, new LoadOptions(WorldRestoreIntent.Branch) {
                IdentityBranchId = Guid.NewGuid(),
                MaximumSnapshotBytes = SnapshotBytes,
            }, cancellationToken).GetAwaiter().GetResult();

            if (requirePrepared)
            {
                Assert.That(preparation.Status, Is.EqualTo(WorldLoadPreparationStatus.Prepared),
                    string.Join("; ", preparation.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            }

            return preparation;
        }

        internal void ReplaceReferencePackage(int minor)
        {
            var schema = _referenceSchema
                ?? throw new InvalidOperationException("This fixture did not select a referenced package asset.");
            var package = CreateReferencePackage(new ContentSchemaReference(
                schema.SchemaId, schema.Major, minor, ContentSchemaScope.Content));
            var packagePath = Path.Combine(_directory, $"{Guid.NewGuid():N}.epkg");
            File.WriteAllBytes(packagePath, package);
            _assets.ReplacePackageAsync(new AssetStoreOptions(
                packagePath, SHA256.HashData(package), _assetRegistration)).GetAwaiter().GetResult();
        }

        internal void Save()
        {
            var receipt = Persistence.SaveAsync(
                World, new SaveOptions(SavePath, SaveDurabilityPolicy.BestEffort)).GetAwaiter().GetResult();
            Assert.That(receipt.Status, Is.AnyOf(SaveStatus.BestEffortCommitted, SaveStatus.DurableCommitted), receipt.Diagnostic?.Message);
            Assert.That(Counters.ResourceReads, Is.Zero);
            Assert.That(Counters.AttachmentReads, Is.Zero);
        }
    }

    private sealed class ReferenceAssetCodec<T>(ContentSchemaReference schema) : IBinarySchemaCodec<T>
    {
        public Guid SchemaId => schema.SchemaId;

        public int SchemaMajor => schema.Major;

        public int SchemaMinor => schema.Minor;

        public BinaryRecordStatus Read(scoped ReadOnlySpan<byte> source, out T value, out int bytesConsumed)
            => throw new AssertionException("Package preflight must inspect the registered tuple without executing its semantic codec.");

        public BinaryRecordStatus Write(in T value, scoped Span<byte> destination, out int bytesWritten)
            => throw new AssertionException("The inspection fixture supplies its literal asset envelope.");
    }

    private sealed class ReferenceAttachmentProvider : IWorldAttachmentStateProvider<ReferenceValue, ReferenceValue>
    {
        private readonly ContentSchemaReference _schema;
        private readonly Guid _targetSchemaId;

        internal ReferenceAttachmentProvider(CompiledContentSchemaRegistry schemas, DataSchema metadata, ContentSchemaReference target)
        {
            _schema = metadata.Schema;
            _targetSchemaId = target.SchemaId;
            var field = WorldAttachmentRecordFieldBinding<ReferenceValue, ReferenceValue>.AssetReference<ReferenceReader, ReferenceReader>(
                metadata.Fields[0], target.SchemaId, [target.Major], default, default);
            var root = WorldAttachmentRecordBinding<ReferenceValue, ReferenceValue>.Create(schemas, _schema, [field]);
            Binding = WorldAttachmentFactoryBinding<ReferenceValue, ReferenceValue>.CreateAggregate(
                new Guid("ef6f6fd2-813a-41f9-a23d-e2ca81766091"), 1, schemas, _schema, root, this);
        }

        internal WorldAttachmentFactoryBinding<ReferenceValue, ReferenceValue> Binding { get; }

        public nint Count(ReferenceValue value, WorldAttachmentCaptureBinding binding, CancellationToken cancellationToken)
            => Encode(value).Length;

        public WorldAttachmentReadResult<ReferenceValue> Read(
            scoped ReadOnlySpan<byte> source, WorldAttachmentReadBinding<ReferenceValue> binding, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reference = new EbinRecordReadView(source).GetRecord(1);
            var author = new ReferenceValue(new WorldAssetReference(
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(reference.GetField(0).Payload)),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(reference.GetField(2).Payload))));
            return binding.Complete(in author, source.Length);
        }

        public void Release(ReferenceValue value, WorldAttachmentReleaseBinding binding) { }

        public ReferenceValue Restore(in ReferenceValue author, WorldAttachmentRestoreBinding binding, CancellationToken cancellationToken)
            => new(author.Reference);

        public ImmutableArray<Diagnostic> Validate(ReferenceValue value, WorldAttachmentValidationBinding binding, CancellationToken cancellationToken)
            => [];

        public nint Write(ReferenceValue value, scoped Span<byte> destination, WorldAttachmentCaptureBinding binding, CancellationToken cancellationToken)
        {
            var encoded = Encode(value);
            encoded.CopyTo(destination);
            return encoded.Length;
        }

        private byte[] Encode(ReferenceValue value)
            => EbinPrimitives.Encode(new EbinRecord(_schema.SchemaId, checked((uint)_schema.Major), checked((uint)_schema.Minor), [
                EbinPrimitives.Record(1, new EbinRecord(
                    new ContentSchemaIdentityAssignment("native/asset-reference").SchemaId, 1, 0, [
                        EbinPrimitives.UInt32(1, checked((uint)value.Reference.AssetId)),
                        EbinPrimitives.Guid(2, _targetSchemaId),
                        EbinPrimitives.UInt32(3, checked((uint)value.Reference.SchemaMajor)),
                    ])),
            ]));
    }

    private readonly struct ReferenceReader : IComponentValueReader<ReferenceValue, WorldAssetReference>
    {
        public WorldAssetReference Get(in ReferenceValue value) => value.Reference;
    }

    private sealed class ReferenceValue(WorldAssetReference reference)
    {
        internal WorldAssetReference Reference { get; } = reference;
    }

    private sealed class SourceUseProbe : IDisposable
    {
        private int _disposeCount;
        private readonly ManualResetEventSlim? _entered;
        private readonly Exception? _failure;
        private readonly IDisposable _inner;

        internal SourceUseProbe(IDisposable inner, ManualResetEventSlim? entered = null, Exception? failure = null)
        {
            _inner = inner;
            _entered = entered;
            _failure = failure;
        }

        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            var count = Interlocked.Increment(ref _disposeCount);
            _entered?.Set();
            _inner.Dispose();

            if ((count == 1) && (_failure is { } failure))
            {
                throw failure;
            }
        }
    }
}
