// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace TerraFX;

internal static class FoundationCodec
{
    internal static EbinStatus Read(scoped ReadOnlySpan<byte> source, FoundationCodecContext context, out EbinRecord? value, out int consumed, out EbinDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        value = null;
        consumed = 0;
        var status = EbinCodec.ScanRecord(source, 1, out var total, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            if (status == EbinStatus.NeedMoreData)
            {
                if (EbinCodec.ReadUnsignedPrefix(source, 32, 8) > (ulong)context.StorageBudget)
                {
                    return EbinCodec.Fail(new Guid(source.Slice(16, 16), bigEndian: true), 0, "retained-storage-budget", out diagnostic, limit: checked((ulong)context.StorageBudget));
                }

                status = ValidatePrefix(source, context, out diagnostic);

                if (status == EbinStatus.NeedMoreData)
                {
                    status = EbinCodec.ValidateRequiredPrefix(source, out diagnostic);
                }

                if (status == EbinStatus.NeedMoreData)
                {
                    diagnostic = new EbinDiagnostic(source.Length >= 32 ? new Guid(source.Slice(16, 16), bigEndian: true) : default, 0, "incomplete-valid-prefix");
                }

                return status;
            }

            return status;
        }

        var bytes = source[..total];
        status = Validate(new EbinRecordView(bytes), context, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        if (total > context.StorageBudget)
        {
            return EbinCodec.Fail(new Guid(bytes.Slice(16, 16), bigEndian: true), 0, "retained-storage-budget", out diagnostic, limit: checked((ulong)context.StorageBudget));
        }

        value = EbinCodec.Stage(bytes);
        consumed = total;
        return EbinStatus.Complete;
    }

    internal static void RequirePersistence(Guid schemaId, FoundationCodecContext context)
    {
        if ((schemaId == FoundationCatalogue.Frame2Id || schemaId == FoundationCatalogue.Frame3Id) && context.Persistence is null)
        {
            ThrowHelper.ThrowArgumentException("Frame I/O requires an explicit persistence binding.", nameof(context));
        }
    }

    internal static EbinStatus Write(EbinRecord value, FoundationCodecContext context, scoped Span<byte> destination, out int written, out EbinDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(context);
        RequirePersistence(value.SchemaId, context);
        written = 0;
        var status = EbinCodec.Prepare(value, out var bytes, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        status = Validate(new EbinRecordView(bytes), context, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        if (bytes.Length > context.StorageBudget)
        {
            return EbinCodec.Fail(value.SchemaId, 0, "retained-storage-budget", out diagnostic, limit: checked((ulong)context.StorageBudget));
        }

        if (destination.Length < bytes.Length)
        {
            diagnostic = new EbinDiagnostic(value.SchemaId, 0, "destination-too-small", Limit: checked((ulong)bytes.Length));
            return EbinStatus.DestinationTooSmall;
        }

        bytes.CopyTo(destination);

        written = bytes.Length;
        return EbinStatus.Complete;
    }

    private static bool IsExtensibleSchema(Guid id) => id == FoundationCatalogue.ProfileId
        || id == FoundationCatalogue.Frame2Id
        || id == FoundationCatalogue.Frame3Id;

    private static EbinStatus Reject(EbinRecordView view, uint tag, string category, out EbinDiagnostic diagnostic, EbinStatus status = EbinStatus.InvalidRecord)
    {
        var index = view.Find(tag);
        var offset = index < 0 ? 0 : BinaryPrimitives.ReadUInt64LittleEndian(view.Entry(index)[8..]);
        diagnostic = new EbinDiagnostic(view.SchemaId, tag, category, offset);
        return status;
    }

    private static EbinStatus Validate(EbinRecordView view, FoundationCodecContext context, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var schema = FoundationCatalogue.Find(view.SchemaId);

        if (schema is null)
        {
            return Reject(view, 0, "unsupported-schema-id", out diagnostic, EbinStatus.UnsupportedSchema);
        }

        if (view.Major != schema.Major || view.Minor != 0)
        {
            var category = view.SchemaId == FoundationCatalogue.ProfileId && view.Major == 1
                ? "unsupported-legacy-profile"
                : view.Major != schema.Major ? "unsupported-schema-major" : "unsupported-schema-minor";
            return Reject(view, 0, category, out diagnostic, EbinStatus.UnsupportedSchema);
        }

        var frame = view.SchemaId == FoundationCatalogue.Frame2Id || view.SchemaId == FoundationCatalogue.Frame3Id;

        if (frame && context.Persistence is null)
        {
            ThrowHelper.ThrowArgumentException("Frame decoding requires an explicit persistence binding.", nameof(context));
        }

        for (var index = 0; index < view.FieldCount; index++)
        {
            var status = ValidateEntry(schema, view.Entry(index), out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }
        }

        foreach (var field in schema.Fields)
        {
            if (view.Find(field.Tag) < 0 && field.Required)
            {
                return Reject(view, field.Tag, "required-field-missing", out diagnostic);
            }
        }

        var requiredKindsStatus = EbinCodec.ValidateRequiredKinds(view, out diagnostic);

        if (requiredKindsStatus != EbinStatus.Complete)
        {
            return requiredKindsStatus;
        }

        // Local/nested rules run in tag order. Context checks only see admitted inputs.
        foreach (var field in schema.Fields)
        {
            if (view.Find(field.Tag) < 0)
            {
                continue;
            }

            var status = ValidateLocal(view, field, context, out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }
        }

        if (view.Find(EbinCodec.ExtensionFieldTag) >= 0)
        {
            var status = ValidateExtensions(view, view.Field(EbinCodec.ExtensionFieldTag), out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }
        }

        return ValidateCross(view, context, out diagnostic);
    }

    private static EbinStatus ValidateCross(EbinRecordView view, FoundationCodecContext context, out EbinDiagnostic diagnostic)
    {
        if (view.SchemaId == FoundationCatalogue.RegistryFieldId)
        {
            return ValidateFieldMetadata(view, out diagnostic);
        }

        if (view.SchemaId == FoundationCatalogue.RegistrySchemaId)
        {
            return ValidateSchemaMetadata(view, out diagnostic);
        }

        if (view.SchemaId == FoundationCatalogue.RegistryManifestId)
        {
            return ValidateRegistry(view, out diagnostic);
        }

        if (view.SchemaId == FoundationCatalogue.KernelRecipeId)
        {
            return ValidateRecipe(view, out diagnostic);
        }

        if (view.SchemaId == FoundationCatalogue.KernelManifestId)
        {
            return ValidateKernel(view, out diagnostic);
        }

        if (view.SchemaId == FoundationCatalogue.ProfileId)
        {
            return ValidateProfile(view, context, out diagnostic);
        }

        return ValidateFrame(view, context, out diagnostic);
    }

    private static EbinStatus ValidateEntry(FoundationSchemaDescriptor schema, scoped ReadOnlySpan<byte> entry, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var tag = BinaryPrimitives.ReadUInt32LittleEndian(entry);
        var kind = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
        var count = entry.Length >= 32 ? BinaryPrimitives.ReadUInt64LittleEndian(entry[24..]) : 0;
        var extensible = IsExtensibleSchema(schema.Id);

        if (schema.Id == FoundationCatalogue.ProfileId && tag == 4)
        {
            diagnostic = new EbinDiagnostic(schema.Id, tag, "profile-reserved-tag");
            return EbinStatus.InvalidRecord;
        }

        if (extensible && tag == EbinCodec.ExtensionFieldTag)
        {
            if (kind != 24)
            {
                diagnostic = new EbinDiagnostic(schema.Id, tag, "extension-array-kind");
                return EbinStatus.InvalidRecord;
            }

            if (flags != 0)
            {
                diagnostic = new EbinDiagnostic(schema.Id, tag, "extension-array-required-flag");
                return EbinStatus.InvalidRecord;
            }

            if (entry.Length >= 32 && (count == 0 || count > EbinCodec.MaximumArrayCount))
            {
                diagnostic = new EbinDiagnostic(schema.Id, tag, "extension-array-count");
                return EbinStatus.InvalidRecord;
            }

            return EbinStatus.Complete;
        }

        foreach (var field in schema.Fields)
        {
            if (field.Tag != tag)
            {
                continue;
            }

            if (kind != field.Kind || flags != (field.Required ? 1 : 0) || (entry.Length >= 32 && (count < field.Minimum || count > field.Maximum)))
            {
                var category = kind > 24 ? "unsupported-required-kind"
                    : kind != field.Kind ? "field-kind-mismatch"
                    : flags != (field.Required ? 1 : 0) ? "field-required-flag"
                    : schema.Id == FoundationCatalogue.ProfileId && tag is 6 or 7 or 8 ? "profile-digest-length"
                    : schema.Id == FoundationCatalogue.KernelRecipeId && tag == 3 ? "kernel-parameters-mismatch" : "field-cardinality";
                diagnostic = new EbinDiagnostic(schema.Id, tag, category);
                return kind > 24 ? EbinStatus.UnsupportedSchema : EbinStatus.InvalidRecord;
            }

            return EbinStatus.Complete;
        }

        diagnostic = new EbinDiagnostic(schema.Id, tag, extensible ? "unsupported-fixed-tag"
            : schema.Id == FoundationCatalogue.KernelRecipeId || schema.Id == FoundationCatalogue.KernelManifestId ? "kernel-extra-field" : "registry-extra-field");
        return extensible ? EbinStatus.UnsupportedSchema : EbinStatus.InvalidRecord;
    }

    private static EbinStatus ValidateExtensions(EbinRecordView parent, scoped ReadOnlySpan<byte> array, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(array));
        long previous = 0;

        for (var index = 0; index < count; index++)
        {
            var child = new EbinRecordView(EbinCodec.ArrayElement(array, index));

            if (child.SchemaId != EbinCodec.ExtensionRecordSchemaId || child.Major != 1 || child.Minor != 0 || child.FieldCount != 2)
            {
                return Reject(parent, EbinCodec.ExtensionFieldTag, "extension-record-schema", out diagnostic);
            }

            var identifierIndex = child.Find(1);
            var payloadIndex = child.Find(2);

            if (identifierIndex < 0 || payloadIndex < 0)
            {
                return Reject(parent, EbinCodec.ExtensionFieldTag, "extension-record-fields", out diagnostic);
            }

            var identifierEntry = child.Entry(identifierIndex);
            var payloadEntry = child.Entry(payloadIndex);

            if (BinaryPrimitives.ReadUInt16LittleEndian(identifierEntry[4..]) != 8
                || BinaryPrimitives.ReadUInt16LittleEndian(identifierEntry[6..]) != 1
                || BinaryPrimitives.ReadUInt64LittleEndian(identifierEntry[24..]) != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(payloadEntry[4..]) != 13
                || BinaryPrimitives.ReadUInt16LittleEndian(payloadEntry[6..]) != 1)
            {
                return Reject(parent, EbinCodec.ExtensionFieldTag, "extension-record-fields", out diagnostic);
            }

            var identifier = BinaryPrimitives.ReadInt64LittleEndian(child.Field(1));

            if (identifier == 0)
            {
                return Reject(parent, EbinCodec.ExtensionFieldTag, "extension-id-zero", out diagnostic);
            }

            if (index != 0 && identifier <= previous)
            {
                return Reject(parent, EbinCodec.ExtensionFieldTag, identifier == previous ? "extension-id-duplicate" : "extension-id-order", out diagnostic);
            }

            previous = identifier;
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateFieldMetadata(EbinRecordView view, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var tag = BinaryPrimitives.ReadUInt32LittleEndian(view.Field(1));
        var kind = BinaryPrimitives.ReadUInt16LittleEndian(view.Field(2));
        var minimum = BinaryPrimitives.ReadUInt64LittleEndian(view.Field(4));
        var maximum = BinaryPrimitives.ReadUInt64LittleEndian(view.Field(5));

        if (tag is 0 or EbinCodec.ExtensionFieldTag
            || minimum > maximum
            || ((EbinCodec.Stride(kind) != 0 || kind == 22) && (minimum != 1 || maximum != 1)))
        {
            return Reject(view, 2, "registry-descriptor-mismatch", out diagnostic);
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateFrame(EbinRecordView view, FoundationCodecContext context, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var binding = context.Persistence;

        if (binding is null)
        {
            ThrowHelper.ThrowArgumentException("Frame decoding requires an explicit persistence binding.", nameof(context));
        }

        var world = new Guid(view.Field(1), bigEndian: true);

        if (world != binding.WorldGuid)
        {
            return Reject(view, 1, "frame-world-mismatch", out diagnostic);
        }

        var nested = new EbinRecordView(view.Field(2));

        if (new Guid(nested.Field(9), bigEndian: true) != binding.Profile.Id.Value)
        {
            return Reject(view, 2, "frame-profile-context", out diagnostic);
        }

        var status = ValidateOrigin(view, context, out diagnostic);

        if (status != EbinStatus.Complete)
        {
            return status;
        }

        var dimensions = view.SchemaId == FoundationCatalogue.Frame2Id ? 2 : 3;
        var minimum = view.Field(5);
        var maximum = view.Field(6);

        for (var axis = 0; axis < dimensions; axis++)
        {
            if (EbinPrimitives.Single(minimum[(axis * 4)..]) > EbinPrimitives.Single(maximum[(axis * 4)..]))
            {
                return Reject(view, 6, "frame-extent-order", out diagnostic);
            }
        }

        if (dimensions == 2 ? !binding.Admits(EbinPrimitives.Vector2(minimum), EbinPrimitives.Vector2(maximum))
            : !binding.Admits(EbinPrimitives.Vector3(minimum), EbinPrimitives.Vector3(maximum)))
        {
            return Reject(view, 6, "frame-owner-extent-policy", out diagnostic);
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateKernel(EbinRecordView view, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;

        if (BinaryPrimitives.ReadUInt32LittleEndian(view.Field(1)) != 1 || BinaryPrimitives.ReadUInt32LittleEndian(view.Field(2)) != 2 || BinaryPrimitives.ReadUInt32LittleEndian(view.Field(3)) != 2)
        {
            return Reject(view, 1, "kernel-code-mismatch", out diagnostic);
        }

        var recipes = view.Field(4);
        var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(recipes));
        Guid previous = default;

        for (var index = 0; index < count; index++)
        {
            var child = new EbinRecordView(EbinCodec.ArrayElement(recipes, index));
            var id = new Guid(child.Field(1), bigEndian: true);
            var order = FoundationCatalogue.CompareRfc(previous, id);

            if (index != 0 && order >= 0)
            {
                return Reject(view, 4, order == 0 ? "kernel-recipe-duplicate" : "kernel-recipe-order", out diagnostic);
            }

            previous = id;
        }

        if (count != FoundationCatalogue.Recipes.Length)
        {
            return Reject(view, 4, "kernel-recipe-set", out diagnostic);
        }

        if (!view.IsCanonical() || BinaryPrimitives.ReadUInt64LittleEndian(view.Bytes[32..]) != (ulong)FoundationCatalogue.KernelBytes.Length
            || !view.Bytes.SequenceEqual(FoundationCatalogue.KernelBytes[..view.Bytes.Length]))
        {
            return Reject(view, 4, "kernel-noncanonical-bytes", out diagnostic);
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateLocal(EbinRecordView view, FoundationFieldDescriptor field, FoundationCodecContext context, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var bytes = view.Field(field.Tag);
        var schemaId = view.SchemaId;

        if (schemaId == FoundationCatalogue.RegistrySchemaId)
        {
            switch (field.Tag)
            {
                case 1:
                {
                    if (new Guid(bytes, bigEndian: true) == Guid.Empty)
                    {
                        return Reject(view, field.Tag, "nonempty-guid", out diagnostic);
                    }

                    break;
                }

                case 2:
                {
                    var major = BinaryPrimitives.ReadUInt32LittleEndian(bytes);

                    if (major is 0 or > int.MaxValue)
                    {
                        return Reject(view, field.Tag, "signed-projection-range", out diagnostic);
                    }

                    break;
                }

                case 3:
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) > int.MaxValue)
                    {
                        return Reject(view, field.Tag, "signed-projection-range", out diagnostic);
                    }

                    break;
                }

                case 4:
                {
                    return ValidateNestedRecords(view, field.Tag, context, out diagnostic);
                }
            }
        }

        if (schemaId == FoundationCatalogue.RegistryManifestId && field.Tag == 1)
        {
            return ValidateNestedRecords(view, field.Tag, context, out diagnostic);
        }

        if (schemaId == FoundationCatalogue.RegistryFieldId)
        {
            switch (field.Tag)
            {
                case 1:
                {
                    var tag = BinaryPrimitives.ReadUInt32LittleEndian(bytes);

                    if (tag is 0 or EbinCodec.ExtensionFieldTag)
                    {
                        return Reject(view, field.Tag, "registry-descriptor-mismatch", out diagnostic);
                    }

                    break;
                }

                case 2:
                {
                    if (BinaryPrimitives.ReadUInt16LittleEndian(bytes) is 0 or > 24)
                    {
                        return Reject(view, field.Tag, "registry-descriptor-mismatch", out diagnostic);
                    }

                    break;
                }

                case 4:
                case 5:
                {
                    if (BinaryPrimitives.ReadUInt64LittleEndian(bytes) > long.MaxValue)
                    {
                        return Reject(view, field.Tag, "signed-projection-range", out diagnostic);
                    }

                    break;
                }
            }
        }

        if (schemaId == FoundationCatalogue.ProfileId)
        {
            if (field.Tag is 1 or 2 or 3)
            {
                var code = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                var expected = field.Tag == 1 ? 1U : 2U;

                if (code != expected)
                {
                    return Reject(view, field.Tag, "unsupported-profile-code", out diagnostic, EbinStatus.UnsupportedSchema);
                }
            }

            if (field.Tag == 5 && bytes[0] is < 8 or > 16)
            {
                return Reject(view, field.Tag, "profile-chunk-range", out diagnostic);
            }

            if (field.Tag == 10 && BinaryPrimitives.ReadInt32LittleEndian(bytes) is < -30 or > 30)
            {
                return Reject(view, field.Tag, "profile-unit-range", out diagnostic);
            }
        }

        if (schemaId == FoundationCatalogue.Frame2Id || schemaId == FoundationCatalogue.Frame3Id)
        {
            switch (field.Tag)
            {
                case 1:
                {
                    if (new Guid(bytes, bigEndian: true) == Guid.Empty)
                    {
                        return Reject(view, field.Tag, "frame-world-empty", out diagnostic);
                    }

                    break;
                }

                case 2:
                {
                    var nested = new EbinRecordView(bytes);
                    var status = ValidateNestedSchema(view, field.Tag, nested.Bytes, out diagnostic);

                    return status == EbinStatus.Complete ? Validate(nested, context, out diagnostic) : status;
                }

                case 4:
                {
                    var revision = BinaryPrimitives.ReadUInt64LittleEndian(bytes);

                    if (revision is 0 or > long.MaxValue)
                    {
                        return Reject(view, field.Tag, "frame-revision-range", out diagnostic);
                    }

                    break;
                }

                case 5:
                case 6:
                {
                    for (var offset = 0; offset < bytes.Length; offset += 4)
                    {
                        if (!float.IsFinite(EbinPrimitives.Single(bytes[offset..])))
                        {
                            return Reject(view, field.Tag, field.Tag == 5 ? "frame-minimum-finite" : "frame-maximum-finite", out diagnostic);
                        }
                    }

                    break;
                }
            }
        }

        if (schemaId == FoundationCatalogue.KernelManifestId)
        {
            if (field.Tag is 1 or 2 or 3)
            {
                var code = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                var expected = field.Tag == 1 ? 1U : 2U;

                if (code != expected && !(code == 1 && field.Tag != 1))
                {
                    return Reject(view, field.Tag, "unsupported-kernel-code", out diagnostic, EbinStatus.UnsupportedSchema);
                }
            }

            if (field.Tag == 4)
            {
                return ValidateNestedRecords(view, field.Tag, context, out diagnostic);
            }
        }

        if (schemaId == FoundationCatalogue.KernelRecipeId)
        {
            if (field.Tag == 1 && new Guid(bytes, bigEndian: true) == Guid.Empty)
            {
                return Reject(view, field.Tag, "nonempty-guid", out diagnostic);
            }

            if (field.Tag == 2)
            {
                var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes);

                if (version is 0 or > int.MaxValue)
                {
                    return Reject(view, field.Tag, "kernel-recipe-version-range", out diagnostic);
                }
            }
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateNestedRecords(EbinRecordView parent, uint tag, FoundationCodecContext context, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var bytes = parent.Field(tag);
        var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes));

        for (var index = 0; index < count; index++)
        {
            var child = new EbinRecordView(EbinCodec.ArrayElement(bytes, index));
            var status = ValidateNestedSchema(parent, tag, child.Bytes, out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }

            status = Validate(child, context, out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateNestedSchema(EbinRecordView parent, uint tag, scoped ReadOnlySpan<byte> bytes, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;

        if (bytes.Length < 32)
        {
            return EbinStatus.Complete;
        }

        var expected = parent.SchemaId == FoundationCatalogue.RegistryManifestId && tag == 1
            ? FoundationCatalogue.RegistrySchemaId
            : parent.SchemaId == FoundationCatalogue.RegistrySchemaId && tag == 4
            ? FoundationCatalogue.RegistryFieldId
            : (parent.SchemaId == FoundationCatalogue.Frame2Id || parent.SchemaId == FoundationCatalogue.Frame3Id) && tag == 2
            ? FoundationCatalogue.ProfileId
            : parent.SchemaId == FoundationCatalogue.KernelManifestId && tag == 4
            ? FoundationCatalogue.KernelRecipeId
            : throw new InvalidOperationException("The trusted nested field has no selected schema.");

        var actual = new Guid(bytes.Slice(16, 16), bigEndian: true);

        if (actual != expected && FoundationCatalogue.Find(actual) is not null)
        {
            return Reject(parent, tag, "nested-schema-mismatch", out diagnostic);
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateOrigin(EbinRecordView view, FoundationCodecContext context, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var binding = context.Persistence;

        if (binding is null)
        {
            ThrowHelper.ThrowArgumentException("Frame decoding requires an explicit persistence binding.", nameof(context));
        }

        var dimensions = view.SchemaId == FoundationCatalogue.Frame2Id ? 2 : 3;
        var origin = view.Field(3);

        for (var axis = 0; axis < dimensions; axis++)
        {
            var offset = EbinPrimitives.Single(origin[((dimensions + axis) * 4)..]);

            if (!float.IsFinite(offset) || !SpatialKernel.IsCanonical(offset, binding.Profile.Settings.ChunkWidth))
            {
                return Reject(view, 3, "canonical-position-offset", out diagnostic);
            }
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidatePrefix(scoped ReadOnlySpan<byte> source, FoundationCodecContext context, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;

        if (source.Length < 16)
        {
            return EbinStatus.NeedMoreData;
        }

        Unsafe.SkipInit(out InlineArray16<byte> idBytesStorage); // Each TryWriteBytes fills all 16 bytes before comparison.
        Span<byte> idBytes = idBytesStorage;

        var idLength = Math.Min(source.Length - 16, 16);
        var candidate = false;

        foreach (var descriptor in FoundationCatalogue.Schemas)
        {
            _ = descriptor.Id.TryWriteBytes(idBytes, bigEndian: true, out _);
            candidate |= source.Slice(16, idLength).SequenceEqual(idBytes[..idLength]);
        }

        if (!candidate)
        {
            diagnostic = new EbinDiagnostic(source.Length >= 32 ? new Guid(source.Slice(16, 16), bigEndian: true) : default, 0, "unsupported-schema-id");
            return EbinStatus.UnsupportedSchema;
        }

        if (source.Length < 32)
        {
            return EbinStatus.NeedMoreData;
        }

        var schemaId = new Guid(source.Slice(16, 16), bigEndian: true);
        var schema = FoundationCatalogue.Find(schemaId);

        if (schema is null)
        {
            diagnostic = new EbinDiagnostic(schemaId, 0, "unsupported-schema-id");
            return EbinStatus.UnsupportedSchema;
        }

        if ((schemaId == FoundationCatalogue.Frame2Id || schemaId == FoundationCatalogue.Frame3Id) && context.Persistence is null)
        {
            ThrowHelper.ThrowArgumentException("Frame decoding requires an explicit persistence binding.", nameof(context));
        }

        Unsafe.SkipInit(out InlineArray4<byte> versionBytesStorage); // WriteUInt32 fills it; Clear prepares the minor check.
        Span<byte> versionBytes = versionBytesStorage;

        BinaryPrimitives.WriteUInt32LittleEndian(versionBytes, schema.Major);
        var majorMatches = EbinCodec.MatchesPrefix(source, 48, versionBytes);
        versionBytes.Clear();

        if (!majorMatches || !EbinCodec.MatchesPrefix(source, 52, versionBytes))
        {
            diagnostic = new EbinDiagnostic(schemaId, 0, !majorMatches ? "unsupported-schema-major" : "unsupported-schema-minor");
            return EbinStatus.UnsupportedSchema;
        }

        if (source.Length < 64)
        {
            return EbinStatus.NeedMoreData;
        }

        // Structural scanning has already checked every currently available entry/range.
        // Do not wait for a payload after an admitted table has proved incompatibility.
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source[12..]));
        var availableEntries = Math.Min(count, Math.Max(0, (source.Length - 64) / 32));

        for (var index = 0; index < availableEntries; index++)
        {
            var entry = source.Slice(64 + (index * 32), 32);
            var status = ValidateEntry(schema, entry, out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }
        }

        if (availableEntries < count && source.Length >= 68 + (availableEntries * 32))
        {
            var partialEntry = source[(64 + (availableEntries * 32))..];
            var tag = BinaryPrimitives.ReadUInt32LittleEndian(partialEntry);

            if (schemaId == FoundationCatalogue.ProfileId && tag == 4)
            {
                diagnostic = new EbinDiagnostic(schemaId, tag, "profile-reserved-tag");
                return EbinStatus.InvalidRecord;
            }

            if (partialEntry.Length >= 8)
            {
                var status = ValidateEntry(schema, partialEntry, out diagnostic);

                if (status != EbinStatus.Complete)
                {
                    return status;
                }
            }
        }

        if (availableEntries != count)
        {
            return EbinStatus.NeedMoreData;
        }

        foreach (var field in schema.Fields)
        {
            if (field.Required)
            {
                var present = false;

                for (var index = 0; index < count; index++)
                {
                    present |= BinaryPrimitives.ReadUInt32LittleEndian(source[(64 + (index * 32))..]) == field.Tag;
                }

                if (!present)
                {
                    diagnostic = new EbinDiagnostic(schemaId, field.Tag, "required-field-missing");
                    return EbinStatus.InvalidRecord;
                }
            }
        }

        var view = new EbinRecordView(source);
        var allRequiredAvailable = true;

        var extensionIndex = view.Find(EbinCodec.ExtensionFieldTag);

        if (extensionIndex >= 0)
        {
            var extensionEntry = view.Entry(extensionIndex);
            var extensionStart = BinaryPrimitives.ReadUInt64LittleEndian(extensionEntry[8..]);
            var extensionLength = BinaryPrimitives.ReadUInt64LittleEndian(extensionEntry[16..]);

            if (extensionStart + extensionLength <= (ulong)source.Length)
            {
                var extensionBytes = source.Slice(checked((int)extensionStart), checked((int)extensionLength));
                var extensionStatus = ValidateExtensions(view, extensionBytes, out diagnostic);

                if (extensionStatus != EbinStatus.Complete)
                {
                    return extensionStatus;
                }
            }
        }

        foreach (var field in schema.Fields)
        {
            var index = view.Find(field.Tag);

            if (index < 0)
            {
                continue;
            }

            var entry = view.Entry(index);
            var start = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            var length = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
            var complete = start + length <= (ulong)source.Length;
            var status = EbinStatus.Complete;

            if (complete)
            {
                status = ValidateLocal(view, field, context, out diagnostic);

                if (status == EbinStatus.Complete
                    && field.Tag == 3
                    && (schemaId == FoundationCatalogue.Frame2Id || schemaId == FoundationCatalogue.Frame3Id))
                {
                    var profileEntry = view.Entry(view.Find(2));

                    if (BinaryPrimitives.ReadUInt64LittleEndian(profileEntry[8..]) + BinaryPrimitives.ReadUInt64LittleEndian(profileEntry[16..]) <= (ulong)source.Length)
                    {
                        status = ValidateOrigin(view, context, out diagnostic);
                    }
                }
            }
            else
            {
                allRequiredAvailable &= !field.Required;
                var bytes = start < (ulong)source.Length ? source[checked((int)start)..] : [];

                if (field.Kind == 22)
                {
                    status = ValidateNestedSchema(view, field.Tag, bytes, out diagnostic);

                    if (status == EbinStatus.Complete)
                    {
                        status = ValidatePrefix(bytes, context, out diagnostic);
                    }
                }
                else if (field.Kind == 24 && bytes.Length >= 8)
                {
                    var children = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes));

                    if (bytes.Length >= 8 + ((children + 1) * 8))
                    {
                        for (var childIndex = 0; childIndex < children; childIndex++)
                        {
                            var childStart = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(8 + (childIndex * 8))..]));
                            var childEnd = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(16 + (childIndex * 8))..]));
                            var childBytes = childStart < bytes.Length ? bytes.Slice(childStart, Math.Min(bytes.Length, childEnd) - childStart) : [];
                            status = ValidateNestedSchema(view, field.Tag, childBytes, out diagnostic);

                            if (status == EbinStatus.Complete)
                            {
                                status = childBytes.Length == childEnd - childStart
                                    ? Validate(new EbinRecordView(childBytes), context, out diagnostic)
                                    : ValidatePrefix(childBytes, context, out diagnostic);
                            }

                            if (status is EbinStatus.InvalidRecord or EbinStatus.UnsupportedSchema)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            if (status is EbinStatus.InvalidRecord or EbinStatus.UnsupportedSchema)
            {
                return status;
            }
        }

        if (allRequiredAvailable)
        {
            var status = ValidateCross(view, context, out diagnostic);

            if (status != EbinStatus.Complete)
            {
                return status;
            }
        }

        return EbinStatus.NeedMoreData;
    }

    private static EbinStatus ValidateProfile(EbinRecordView view, FoundationCodecContext context, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var id = new Guid(view.Field(9), bigEndian: true);

        if (id == FoundationCatalogue.LegacyProfileId)
        {
            return Reject(view, 9, "profile-legacy-identity", out diagnostic);
        }

        var profile = context.FindProfile(id);

        if (profile is null || !SpatialProfiles.TryGetSettings(profile.Id, out var registered))
        {
            return Reject(view, 9, "unsupported-profile-identity", out diagnostic, EbinStatus.UnsupportedSchema);
        }

        var meters = BinaryPrimitives.ReadInt32LittleEndian(view.Field(10));
        var chunk = view.Field(5)[0];

        if (meters != profile.Settings.MetersPerUnitExponent || chunk != profile.Settings.ChunkSizeExponent || registered != profile.Settings)
        {
            return Reject(view, 9, "profile-identity-binding", out diagnostic);
        }

        for (uint tag = 6; tag <= 8; tag++)
        {
            var expected = tag == 6 ? profile.SettingsDigest : tag == 7 ? profile.RegistryDigest : profile.KernelDigest;

            if (!view.Field(tag).SequenceEqual(expected))
            {
                return Reject(view, tag, "profile-digest-binding", out diagnostic);
            }
        }

        if (context.PackageProfile is not null && !context.PackageProfile.Matches(profile))
        {
            return Reject(view, 9, "profile-package-binding", out diagnostic);
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateRecipe(EbinRecordView view, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var recipe = FoundationCatalogue.FindRecipe(new Guid(view.Field(1), bigEndian: true));

        if (recipe is null)
        {
            return Reject(view, 1, "unsupported-kernel-recipe", out diagnostic, EbinStatus.UnsupportedSchema);
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(view.Field(2)) != 1)
        {
            return Reject(view, 2, "unsupported-kernel-recipe-version", out diagnostic, EbinStatus.UnsupportedSchema);
        }

        if (!view.Field(3).SequenceEqual(recipe.Parameters))
        {
            return Reject(view, 3, "kernel-parameters-mismatch", out diagnostic);
        }

        if (!view.IsCanonical())
        {
            return Reject(view, 3, "kernel-noncanonical-bytes", out diagnostic);
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateRegistry(EbinRecordView view, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var schemas = view.Field(1);
        var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(schemas));
        Guid previous = default;

        for (var index = 0; index < count; index++)
        {
            var child = new EbinRecordView(EbinCodec.ArrayElement(schemas, index));
            var id = new Guid(child.Field(1), bigEndian: true);
            var order = FoundationCatalogue.CompareRfc(previous, id);

            if (index != 0 && order >= 0)
            {
                return Reject(view, 1, order == 0 ? "registry-schema-duplicate" : "registry-schema-order", out diagnostic);
            }

            previous = id;
        }

        if (count != FoundationCatalogue.Schemas.Length)
        {
            return Reject(view, 1, "registry-schema-set", out diagnostic);
        }

        if (!view.IsCanonical() || BinaryPrimitives.ReadUInt64LittleEndian(view.Bytes[32..]) != (ulong)FoundationCatalogue.RegistryBytes.Length
            || !view.Bytes.SequenceEqual(FoundationCatalogue.RegistryBytes[..view.Bytes.Length]))
        {
            return Reject(view, 1, "registry-noncanonical-bytes", out diagnostic);
        }

        return EbinStatus.Complete;
    }

    private static EbinStatus ValidateSchemaMetadata(EbinRecordView view, out EbinDiagnostic diagnostic)
    {
        diagnostic = default;
        var id = new Guid(view.Field(1), bigEndian: true);
        var schema = FoundationCatalogue.Find(id);

        if (schema is null)
        {
            return Reject(view, 1, "unsupported-schema-id", out diagnostic, EbinStatus.UnsupportedSchema);
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(view.Field(2)) != schema.Major || BinaryPrimitives.ReadUInt32LittleEndian(view.Field(3)) != 0)
        {
            return Reject(view, 2, "registry-descriptor-mismatch", out diagnostic);
        }

        var fields = view.Field(4);
        var count = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(fields));
        uint previous = 0;

        for (var index = 0; index < count; index++)
        {
            var child = new EbinRecordView(EbinCodec.ArrayElement(fields, index));
            var tag = BinaryPrimitives.ReadUInt32LittleEndian(child.Field(1));

            if (tag <= previous)
            {
                var duplicate = false;

                for (var earlier = 0; earlier < index; earlier++)
                {
                    var prior = new EbinRecordView(EbinCodec.ArrayElement(fields, earlier));
                    duplicate |= BinaryPrimitives.ReadUInt32LittleEndian(prior.Field(1)) == tag;
                }

                return Reject(view, 4, duplicate ? "registry-field-duplicate" : "registry-field-order", out diagnostic);
            }

            previous = tag;
        }

        if (count != schema.Fields.Length)
        {
            return Reject(view, 4, "registry-field-set", out diagnostic);
        }

        for (var index = 0; index < count; index++)
        {
            var child = new EbinRecordView(EbinCodec.ArrayElement(fields, index));
            var field = schema.Fields[index];

            if (BinaryPrimitives.ReadUInt32LittleEndian(child.Field(1)) != field.Tag
                || BinaryPrimitives.ReadUInt16LittleEndian(child.Field(2)) != field.Kind
                || child.Field(3)[0] != (field.Required ? 1 : 0)
                || BinaryPrimitives.ReadUInt64LittleEndian(child.Field(4)) != field.Minimum
                || BinaryPrimitives.ReadUInt64LittleEndian(child.Field(5)) != field.Maximum)
            {
                return Reject(view, 4, "registry-descriptor-mismatch", out diagnostic);
            }
        }

        if (!view.IsCanonical())
        {
            return Reject(view, 4, "registry-noncanonical-bytes", out diagnostic);
        }

        return EbinStatus.Complete;
    }
}
