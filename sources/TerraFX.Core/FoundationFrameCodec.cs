// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX;

internal static class FoundationFrameCodec
{
    internal static EbinRecord Capture(in SpatialFrame2 frame, FoundationPersistenceBinding binding, uint currentOwnerId, long currentBindingRevision, FoundationFrame2Data? previous = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.ValidateCapture(frame.OwnerId, currentOwnerId, currentBindingRevision);
        SpatialKernel.ValidateFrame(frame);

        if (frame.ProfileId != binding.Profile.Id || frame.Settings != binding.Profile.Settings)
        {
            ThrowHelper.ThrowArgumentException("The frame profile disagrees with its owning persistence binding.", nameof(frame));
        }

        if (previous is not null && (previous.WorldGuid != binding.WorldGuid || previous.Profile.Id != binding.Profile.Id))
        {
            ThrowHelper.ThrowInvalidOperationException("Retained persistence data belongs to a different world/profile.");
        }

        var profile = MergeExtensions(binding.Profile.CreateRecord(), previous is null ? null : EbinCodec.Stage(previous.Record.GetField(2).Payload));

        var record = new EbinRecord(FoundationCatalogue.Frame2Id, 1, 0, [
            EbinPrimitives.Guid(1, binding.WorldGuid), EbinPrimitives.Record(2, profile), EbinPrimitives.Position(3, frame.Origin),
            EbinPrimitives.UInt64(4, checked((ulong)frame.Revision)),
            EbinPrimitives.Geometry(5, frame.Minimum), EbinPrimitives.Geometry(6, frame.Maximum),
        ]);

        return ValidateCapture(MergeExtensions(record, previous?.Record), binding);
    }

    internal static EbinRecord Capture(in SpatialFrame3 frame, FoundationPersistenceBinding binding, uint currentOwnerId, long currentBindingRevision, FoundationFrame3Data? previous = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.ValidateCapture(frame.OwnerId, currentOwnerId, currentBindingRevision);
        SpatialKernel.ValidateFrame(frame);

        if (frame.ProfileId != binding.Profile.Id || frame.Settings != binding.Profile.Settings)
        {
            ThrowHelper.ThrowArgumentException("The frame profile disagrees with its owning persistence binding.", nameof(frame));
        }

        if (previous is not null && (previous.WorldGuid != binding.WorldGuid || previous.Profile.Id != binding.Profile.Id))
        {
            ThrowHelper.ThrowInvalidOperationException("Retained persistence data belongs to a different world/profile.");
        }

        var profile = MergeExtensions(binding.Profile.CreateRecord(), previous is null ? null : EbinCodec.Stage(previous.Record.GetField(2).Payload));

        var record = new EbinRecord(FoundationCatalogue.Frame3Id, 1, 0, [
            EbinPrimitives.Guid(1, binding.WorldGuid), EbinPrimitives.Record(2, profile), EbinPrimitives.Position(3, frame.Origin),
            EbinPrimitives.UInt64(4, checked((ulong)frame.Revision)),
            EbinPrimitives.Geometry(5, frame.Minimum), EbinPrimitives.Geometry(6, frame.Maximum),
        ]);

        return ValidateCapture(MergeExtensions(record, previous?.Record), binding);
    }

    internal static EbinStatus Read2(scoped ReadOnlySpan<byte> source, FoundationCodecContext context, out FoundationFrame2Data? value, out int consumed, out EbinDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        FoundationCodec.RequirePersistence(FoundationCatalogue.Frame2Id, context);
        value = null;
        var status = FoundationCodec.Read(source, context, out var record, out consumed, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        if (record is null || record.SchemaId != FoundationCatalogue.Frame2Id)
        {
            consumed = 0;
            return EbinCodec.Fail(record?.SchemaId ?? default, 0, "frame-dimension-schema", out diagnostic);
        }

        var binding = context.Persistence;

        if (binding is null)
        {
            ThrowHelper.ThrowArgumentException("An explicit persistence binding is required.", nameof(context));
        }

        value = new FoundationFrame2Data(record, binding);
        return EbinStatus.Complete;
    }

    internal static EbinStatus Read3(scoped ReadOnlySpan<byte> source, FoundationCodecContext context, out FoundationFrame3Data? value, out int consumed, out EbinDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        FoundationCodec.RequirePersistence(FoundationCatalogue.Frame3Id, context);
        value = null;
        var status = FoundationCodec.Read(source, context, out var record, out consumed, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        if (record is null || record.SchemaId != FoundationCatalogue.Frame3Id)
        {
            consumed = 0;
            return EbinCodec.Fail(record?.SchemaId ?? default, 0, "frame-dimension-schema", out diagnostic);
        }

        var binding = context.Persistence;

        if (binding is null)
        {
            ThrowHelper.ThrowArgumentException("An explicit persistence binding is required.", nameof(context));
        }

        value = new FoundationFrame3Data(record, binding);
        return EbinStatus.Complete;
    }

    private static EbinRecord MergeExtensions(EbinRecord known, EbinRecord? previous)
    {
        if (previous is null)
        {
            return known;
        }

        var fields = new List<EbinField>(known.Fields);

        foreach (var field in previous.Fields)
        {
            var present = false;

            foreach (var current in known.Fields)
            {
                present |= current.Tag == field.Tag;
            }

            if (!present)
            {
                fields.Add(field);
            }
        }

        return new EbinRecord(known.SchemaId, known.Major, known.Minor, [.. fields]);
    }

    private static EbinRecord ValidateCapture(EbinRecord record, FoundationPersistenceBinding binding)
    {
        var context = new FoundationCodecContext([binding.Profile], binding);
        var destination = new byte[EbinCodec.MaximumRecordBytes];

        var status = FoundationCodec.Write(record, context, destination, out _, out var diagnostic);

        if (status != EbinStatus.Complete)
        {
            ThrowHelper.ThrowArgumentException($"The committed capture is not admitted: {diagnostic.Category}.", nameof(record));
        }

        return record;
    }
}
