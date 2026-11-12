// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal static class AnimationAssetReader
{
    private static readonly Guid s_jointSchemaId = new ContentSchemaIdentityAssignment("native/joint").SchemaId;
    private static readonly Guid s_localTransformSchemaId = new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId;
    private static readonly Guid s_skeletonSchemaId = new ContentSchemaIdentityAssignment("native/skeleton").SchemaId;

    internal static SkeletonAsset ReadSkeleton(scoped EbinRecordReadView record, AssetCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateHeader(record, s_skeletonSchemaId, fieldCounts: [1, 2]);

        var jointField = GetRequiredRecordArray(record, tag: 1);

        if (jointField.Count is 0 or > 256)
        {
            throw new InvalidDataException("A Skeleton must contain between one and 256 joints.");
        }

        var joints = ImmutableArray.CreateBuilder<SkeletonJoint>(checked((int)jointField.Count));
        var bindModels = new Matrix4x4[checked((int)jointField.Count)];
        var jointIndices = new Dictionary<Guid, int>(checked((int)jointField.Count));
        var hierarchyDepths = new int[checked((int)jointField.Count)];

        for (var index = 0; index < joints.Capacity; index++)
        {
            var jointRecord = record.GetRecordArrayElement(1, index);
            ValidateHeader(jointRecord, s_jointSchemaId, fieldCounts: [4]);
            var jointId = ReadGuid(jointRecord, tag: 1);
            var parentIndex = ReadInt32(jointRecord, tag: 2);
            (var bindTranslation, var bindRotation, var bindScale) = ReadLocalTransform(jointRecord.GetRecord(3));
            var inverseBind = ReadMatrix4x4(jointRecord, tag: 4);

            if (jointId == Guid.Empty)
            {
                throw new InvalidDataException("A Skeleton JointId cannot be empty.");
            }

            if (!jointIndices.TryAdd(jointId, index))
            {
                throw new InvalidDataException("Skeleton JointIds must be unique.");
            }

            if ((parentIndex < -1) || (parentIndex >= index))
            {
                throw new InvalidDataException("A Skeleton parent must be -1 or an earlier joint index.");
            }

            var depth = (parentIndex < 0) ? 1 : checked(hierarchyDepths[parentIndex] + 1);

            if (depth > 256)
            {
                throw new InvalidDataException("A Skeleton hierarchy cannot exceed depth 256.");
            }

            hierarchyDepths[index] = depth;
            var localMatrix = Matrix4x4.CreateScale(bindScale)
                * Matrix4x4.CreateFromQuaternion(bindRotation)
                * Matrix4x4.CreateTranslation(bindTranslation);
            bindModels[index] = (parentIndex < 0) ? localMatrix : localMatrix * bindModels[parentIndex];
            ValidateInverseBind(inverseBind, bindModels[index]);
            joints.Add(new SkeletonJoint(
                jointId,
                parentIndex,
                bindTranslation,
                bindRotation,
                bindScale,
                inverseBind));
        }

        Guid? rootMotionJointId = null;

        if (record.TryGetField(2, out var rootMotionField))
        {
            if (rootMotionField.IsRequired
                || (rootMotionField.Kind != 11)
                || (rootMotionField.Count != 1)
                || (rootMotionField.Payload.Length != 16))
            {
                throw new InvalidDataException("Skeleton RootMotionJoint has an invalid optional Joint reference.");
            }

            var selectedRoot = new Guid(rootMotionField.Payload, bigEndian: true);

            if (!jointIndices.TryGetValue(selectedRoot, out var rootIndex)
                || (joints[rootIndex].ParentIndex != -1)
                || (joints[rootIndex].BindScale != Vector3.One))
            {
                throw new InvalidDataException("Skeleton RootMotionJoint must identify a declared parentless joint with unit bind scale.");
            }

            rootMotionJointId = selectedRoot;
        }

        return new SkeletonAsset(
            context.RootAssetId,
            rootMotionJointId,
            joints.MoveToImmutable(),
            [.. record.EncodedBytes]);
    }

    private static EbinFieldReadView GetRequiredField(EbinRecordReadView record, uint tag, ushort kind, ulong count = 1)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != kind)
            || (field.Count != count))
        {
            throw new InvalidDataException($"Animation record field {tag} does not match its required wire shape.");
        }

        return field;
    }

    private static EbinFieldReadView GetRequiredRecordArray(EbinRecordReadView record, uint tag)
    {
        if (!record.TryGetField(tag, out var field)
            || !field.IsRequired
            || (field.Kind != 24))
        {
            throw new InvalidDataException($"Animation record field {tag} does not contain a required record array.");
        }

        return field;
    }

    private static bool IsScaleValid(Vector3 scale)
        => scale.IsFinite().All()
        && (float.Abs(scale.X) >= 0.0001f)
        && (float.Abs(scale.X) <= 1000.0f)
        && (float.Abs(scale.Y) >= 0.0001f)
        && (float.Abs(scale.Y) <= 1000.0f)
        && (float.Abs(scale.Z) >= 0.0001f)
        && (float.Abs(scale.Z) <= 1000.0f);

    private static Guid ReadGuid(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 11);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"Animation record field {tag} does not contain one GUID.");
        }

        return new Guid(field.Payload, bigEndian: true);
    }

    private static int ReadInt32(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 7);

        if (field.Payload.Length != sizeof(int))
        {
            throw new InvalidDataException($"Animation record field {tag} does not contain one Int32.");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(field.Payload);
    }

    private static (Vector3 Translation, Quaternion Rotation, Vector3 Scale) ReadLocalTransform(EbinRecordReadView record)
    {
        ValidateHeader(record, s_localTransformSchemaId, fieldCounts: [3]);
        var translation = ReadVector3(record, tag: 1);
        var rotation = ReadQuaternion(record, tag: 2);
        var scale = ReadVector3(record, tag: 3);

        if (!translation.IsFinite().All())
        {
            throw new InvalidDataException("A Skeleton bind-local translation must be finite.");
        }

        var rotationLengthSquared = Quaternion.Dot(rotation, rotation);

        if (!rotation.IsFinite().All()
            || !float.IsFinite(rotationLengthSquared)
            || (rotationLengthSquared <= 1.0e-12f))
        {
            throw new InvalidDataException("A Skeleton bind-local rotation must have a finite nonzero norm.");
        }

        if (!IsScaleValid(scale))
        {
            throw new InvalidDataException("A Skeleton bind-local scale must be finite with magnitudes in [0.0001,1000].");
        }

        return (translation, Quaternion.Normalize(rotation), scale);
    }

    private static Matrix4x4 ReadMatrix4x4(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 19);

        if (field.Payload.Length != 64)
        {
            throw new InvalidDataException($"Animation record field {tag} does not contain one Matrix4x4.");
        }

        var rows = new Vector4[4];

        for (var row = 0; row < rows.Length; row++)
        {
            var offset = row * 16;
            rows[row] = new Vector4(
                EbinPrimitives.Single(field.Payload[offset..]),
                EbinPrimitives.Single(field.Payload[(offset + 4)..]),
                EbinPrimitives.Single(field.Payload[(offset + 8)..]),
                EbinPrimitives.Single(field.Payload[(offset + 12)..]));
        }

        var value = new Matrix4x4(rows[0], rows[1], rows[2], rows[3]);
        return value.IsFinite().All()
            ? value
            : throw new InvalidDataException($"Animation record field {tag} must be finite.");
    }

    private static Quaternion ReadQuaternion(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 17);

        if (field.Payload.Length != 16)
        {
            throw new InvalidDataException($"Animation record field {tag} does not contain one Quaternion.");
        }

        return new Quaternion(
            EbinPrimitives.Single(field.Payload),
            EbinPrimitives.Single(field.Payload[4..]),
            EbinPrimitives.Single(field.Payload[8..]),
            EbinPrimitives.Single(field.Payload[12..]));
    }

    private static Vector3 ReadVector3(EbinRecordReadView record, uint tag)
    {
        var field = GetRequiredField(record, tag, kind: 15);

        if (field.Payload.Length != 12)
        {
            throw new InvalidDataException($"Animation record field {tag} does not contain one Vector3.");
        }

        return EbinPrimitives.Vector3(field.Payload);
    }

    private static void ValidateHeader(
        EbinRecordReadView record,
        Guid schemaId,
        ReadOnlySpan<int> fieldCounts)
    {
        var header = record.Header;

        if ((header.SchemaId != schemaId) || (header.Major != 1) || (header.Minor != 0))
        {
            throw new NotSupportedException("The selected Animation asset schema version is unsupported.");
        }

        foreach (var fieldCount in fieldCounts)
        {
            if (header.FieldCount == fieldCount)
            {
                return;
            }
        }

        throw new InvalidDataException("An Animation asset record has an invalid field count.");
    }

    private static void ValidateInverseBind(Matrix4x4 inverseBind, Matrix4x4 bindModel)
    {
        var product = inverseBind * bindModel;

        if (!product.IsFinite().All())
        {
            throw new InvalidDataException("Skeleton inverse-bind and bind-model multiplication is nonfinite.");
        }

        var identity = Matrix4x4.Identity;

        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                if (float.Abs(product[row, column] - identity[row, column]) > 1.0e-5f)
                {
                    throw new InvalidDataException("A Skeleton inverse-bind matrix does not invert its bind-model transform within tolerance.");
                }
            }
        }
    }
}
