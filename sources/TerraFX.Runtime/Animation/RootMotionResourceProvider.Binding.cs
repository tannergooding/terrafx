// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Globalization;
using TerraFX.Entities;
using TerraFX.Numerics;

namespace TerraFX.Animation;

internal sealed partial class RootMotionResourceProvider
{
    private static WorldResourceFactoryBinding<RootMotionRequest, RootMotionResourceAuthor> CreateBinding(
        CompiledContentSchemaRegistry schemas, RootMotionResourceProvider defaults)
    {
        var root = RootMotionResourceSchemas.GetRequired(schemas, "animation/root-motion-resource");
        var slot = RootMotionResourceSchemas.GetRequired(schemas, "animation/root-motion-slot");
        var source = RootMotionResourceSchemas.GetRequired(schemas, "animation/root-motion-source");
        var sourceStorage = new ComponentStorageDeclaration(40, [
            Leaf("ClipSlotId", 0, 16), Leaf("ClipAssetId", 16, 4), Leaf("PlaySequence", 20, 8),
            Leaf("LoopOrdinal", 28, 8), Leaf("Weight", 36, 4),
        ], []);
        var sourceBinding = WorldResourceRecordBinding<RootMotionSource, RootMotionSourceAuthor>.Create(
            schemas, source.Schema, sourceStorage, [
                WorldResourceRecordFieldBinding<RootMotionSource, RootMotionSourceAuthor>.Value<Guid, Source1, Source1>(
                    source.Fields[0], [Path("ClipSlotId")], default, default),
                WorldResourceRecordFieldBinding<RootMotionSource, RootMotionSourceAuthor>.AssetReference<Source2, Source2>(
                    source.Fields[1], [Path("ClipAssetId")], RootMotionResourceSchemas.Reference("native/clip").SchemaId, [1], default, default),
                WorldResourceRecordFieldBinding<RootMotionSource, RootMotionSourceAuthor>.Value<long, Source3, Source3>(
                    source.Fields[2], [Path("PlaySequence")], default, default),
                WorldResourceRecordFieldBinding<RootMotionSource, RootMotionSourceAuthor>.Value<long, Source4, Source4>(
                    source.Fields[3], [Path("LoopOrdinal")], default, default),
                WorldResourceRecordFieldBinding<RootMotionSource, RootMotionSourceAuthor>.Value<float, Source5, Source5>(
                    source.Fields[4], [Path("Weight")], default, default),
            ], default(RootMotionResourceValidation.SourceValidator));
        var leaves = ImmutableArray.CreateBuilder<ComponentStorageLeaf>();
        leaves.Add(Leaf("Present", 0, 1));
        leaves.Add(Leaf("WorldGuid", 1, 16, ComponentStoragePurpose.DeclaredRuntimeOnly));
        leaves.Add(Leaf("PlayerId", 17, 16));
        leaves.Add(Leaf("PlayerLineage", 33, 16));
        leaves.Add(Leaf("PlayerBranch", 49, 16));
        leaves.Add(Leaf("ActorId", 65, 16));
        leaves.Add(Leaf("ActorLineage", 81, 16));
        leaves.Add(Leaf("ActorBranch", 97, 16));
        leaves.Add(Leaf("Tick", 113, 8));
        leaves.Add(Leaf("TranslationX", 121, 4));
        leaves.Add(Leaf("TranslationY", 125, 4));
        leaves.Add(Leaf("TranslationZ", 129, 4));
        leaves.Add(Leaf("RotationX", 133, 4));
        leaves.Add(Leaf("RotationY", 137, 4));
        leaves.Add(Leaf("RotationZ", 141, 4));
        leaves.Add(Leaf("RotationW", 145, 4));
        leaves.Add(Leaf("Policy", 149, 1));
        leaves.Add(Leaf("SourceCount", 150, 1, ComponentStoragePurpose.DeclaredRuntimeOnly));
        var sourcePaths = ImmutableArray.CreateBuilder<ComponentStoragePath>(20);

        for (var index = 0; index < 4; index++)
        {
            var prefix = "Source" + index.ToString(CultureInfo.InvariantCulture);

            foreach (var child in sourceStorage.Leaves)
            {
                var name = child.Path.Segments[0].MemberName
                    ?? throw new InvalidOperationException("The source storage leaf must have its explicit member path.");
                var path = new ComponentStoragePath([ComponentStorageSegment.Member(prefix), ComponentStorageSegment.Member(name)]);
                leaves.Add(new ComponentStorageLeaf(
                    path, child.Kind, 151 + (index * 40) + child.ByteOffset, child.ByteExtent, 0, 0, ComponentStoragePurpose.SchemaValue));
                sourcePaths.Add(path);
            }
        }

        leaves.Add(Leaf("Controller", 311, 4));
        var storage = new ComponentStorageDeclaration(315, leaves.ToImmutable(), []);
        var slotBinding = WorldResourceRecordBinding<RootMotionRequest, RootMotionSlotAuthor>.Create(
            schemas, slot.Schema, storage, [
                WorldResourceRecordFieldBinding<RootMotionRequest, RootMotionSlotAuthor>.Value<bool, Slot1, Slot1>(
                    slot.Fields[0], [Path("Present")], default, default),
                Optional<Guid, Slot2>(slot, 2, [Path("PlayerId")], ContentReferenceKind.HistoricalEntity),
                Optional<Guid, Slot3>(slot, 3, [Path("PlayerLineage")]),
                Optional<Guid, Slot4>(slot, 4, [Path("PlayerBranch")]),
                Optional<Guid, Slot5>(slot, 5, [Path("ActorId")], ContentReferenceKind.HistoricalEntity),
                Optional<Guid, Slot6>(slot, 6, [Path("ActorLineage")]),
                Optional<Guid, Slot7>(slot, 7, [Path("ActorBranch")]),
                Optional<long, Slot8>(slot, 8, [Path("Tick")]),
                Optional<Vector3, Slot9>(slot, 9, [Path("TranslationX"), Path("TranslationY"), Path("TranslationZ")]),
                Optional<Quaternion, Slot10>(slot, 10, [Path("RotationX"), Path("RotationY"), Path("RotationZ"), Path("RotationW")]),
                WorldResourceRecordFieldBinding<RootMotionRequest, RootMotionSlotAuthor>.OptionalRecords<RootMotionSource, RootMotionSourceAuthor, Slot11, Slot11>(
                    slot.Fields[10], sourcePaths.MoveToImmutable(), sourceBinding, default, default),
                Optional<byte, Slot12>(slot, 12, [Path("Policy")]),
                WorldResourceRecordFieldBinding<RootMotionRequest, RootMotionSlotAuthor>.OptionalAssetReference<Slot13, Slot13>(
                    slot.Fields[12], [Path("Controller")], RootMotionResourceSchemas.Reference("native/animation-controller").SchemaId, [1], default, default),
            ], default(RootMotionResourceValidation.SlotValidator));
        var schema = WorldResourceSchema<RootMotionRequest>.FromRegistry(schemas, root.Schema);
        var values = WorldResourceFieldBinding<RootMotionRequest, RootMotionResourceAuthor>.PartitionRecords<RootMotionSlotAuthor, Values>(
            schema.Fields[0], slotBinding, default);
        return WorldResourceFactoryBinding<RootMotionRequest, RootMotionResourceAuthor>.CreateRecords(
            new ContentSchemaIdentityAssignment("resource/animation/root-motion/factory/v1").SchemaId,
            1, schema, storage, values, defaults, default(RootMotionResourceValidation.ResourceValidator));
    }

    private static ComponentStorageLeaf Leaf(
        string name, int offset, int extent, ComponentStoragePurpose purpose = ComponentStoragePurpose.SchemaValue)
        => new(Path(name), ComponentStorageKind.Value, offset, extent, 0, 0, purpose);

    private static WorldResourceRecordFieldBinding<RootMotionRequest, RootMotionSlotAuthor> Optional<V, TReader>(
        DataSchema slot,
        uint tag,
        ImmutableArray<ComponentStoragePath> paths,
        ContentReferenceKind reference = ContentReferenceKind.None)
        where V : unmanaged
        where TReader : struct, IComponentOptionalReader<RootMotionRequest, V>, IComponentOptionalReader<RootMotionSlotAuthor, V>
        => WorldResourceRecordFieldBinding<RootMotionRequest, RootMotionSlotAuthor>.Optional<V, TReader, TReader>(
            slot.Fields[checked((int)tag - 1)], reference, paths, default, default);

    private static ComponentStoragePath Path(string name) => new([ComponentStorageSegment.Member(name)]);

    private readonly struct Slot1 :
        IComponentValueReader<RootMotionRequest, bool>, IComponentValueReader<RootMotionSlotAuthor, bool>
    {
        public bool Get(in RootMotionRequest value) => value.IsValid;

        public bool Get(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot10 :
        IComponentOptionalReader<RootMotionRequest, Quaternion>, IComponentOptionalReader<RootMotionSlotAuthor, Quaternion>
    {
        public Quaternion GetPresent(in RootMotionRequest value) => value.Rotation;

        public Quaternion GetPresent(in RootMotionSlotAuthor value) => value.Rotation ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot11 :
        IWorldResourceOptionalArrayReader<RootMotionRequest, RootMotionSource>,
        IWorldResourceOptionalArrayReader<RootMotionSlotAuthor, RootMotionSourceAuthor>
    {
        public int GetCount(in RootMotionRequest value) => value.SourceCount;

        public int GetCount(in RootMotionSlotAuthor value) => (value.Sources ?? throw new InvalidOperationException("The slot is absent.")).Length;

        public RootMotionSource GetElement(in RootMotionRequest value, int index) => value.GetSource(index);

        public RootMotionSourceAuthor GetElement(in RootMotionSlotAuthor value, int index) => (value.Sources ?? throw new InvalidOperationException("The slot is absent."))[index];

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot12 :
        IComponentOptionalReader<RootMotionRequest, byte>, IComponentOptionalReader<RootMotionSlotAuthor, byte>
    {
        public byte GetPresent(in RootMotionRequest value) => (byte)value.ExtractionPolicy;

        public byte GetPresent(in RootMotionSlotAuthor value) => value.ExtractionPolicy ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot13 :
        IComponentOptionalReader<RootMotionRequest, WorldAssetReference>, IComponentOptionalReader<RootMotionSlotAuthor, WorldAssetReference>
    {
        public WorldAssetReference GetPresent(in RootMotionRequest value) => new(value.ControllerAssetId, 1);

        public WorldAssetReference GetPresent(in RootMotionSlotAuthor value) => value.Controller ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot2 :
        IComponentOptionalReader<RootMotionRequest, Guid>, IComponentOptionalReader<RootMotionSlotAuthor, Guid>
    {
        public Guid GetPresent(in RootMotionRequest value) => value.Player.EntityId;

        public Guid GetPresent(in RootMotionSlotAuthor value) => value.PlayerId ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot3 :
        IComponentOptionalReader<RootMotionRequest, Guid>, IComponentOptionalReader<RootMotionSlotAuthor, Guid>
    {
        public Guid GetPresent(in RootMotionRequest value) => value.Player.CreationLineageId;

        public Guid GetPresent(in RootMotionSlotAuthor value) => value.PlayerCreationLineageId ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot4 :
        IComponentOptionalReader<RootMotionRequest, Guid>, IComponentOptionalReader<RootMotionSlotAuthor, Guid>
    {
        public Guid GetPresent(in RootMotionRequest value) => value.Player.CreationBranchId;

        public Guid GetPresent(in RootMotionSlotAuthor value) => value.PlayerCreationBranchId ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot5 :
        IComponentOptionalReader<RootMotionRequest, Guid>, IComponentOptionalReader<RootMotionSlotAuthor, Guid>
    {
        public Guid GetPresent(in RootMotionRequest value) => value.Actor.EntityId;

        public Guid GetPresent(in RootMotionSlotAuthor value) => value.ActorId ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot6 :
        IComponentOptionalReader<RootMotionRequest, Guid>, IComponentOptionalReader<RootMotionSlotAuthor, Guid>
    {
        public Guid GetPresent(in RootMotionRequest value) => value.Actor.CreationLineageId;

        public Guid GetPresent(in RootMotionSlotAuthor value) => value.ActorCreationLineageId ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot7 :
        IComponentOptionalReader<RootMotionRequest, Guid>, IComponentOptionalReader<RootMotionSlotAuthor, Guid>
    {
        public Guid GetPresent(in RootMotionRequest value) => value.Actor.CreationBranchId;

        public Guid GetPresent(in RootMotionSlotAuthor value) => value.ActorCreationBranchId ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot8 :
        IComponentOptionalReader<RootMotionRequest, long>, IComponentOptionalReader<RootMotionSlotAuthor, long>
    {
        public long GetPresent(in RootMotionRequest value) => value.TickIndex;

        public long GetPresent(in RootMotionSlotAuthor value) => value.TickIndex ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Slot9 :
        IComponentOptionalReader<RootMotionRequest, Vector3>, IComponentOptionalReader<RootMotionSlotAuthor, Vector3>
    {
        public Vector3 GetPresent(in RootMotionRequest value) => value.Translation;

        public Vector3 GetPresent(in RootMotionSlotAuthor value) => value.Translation ?? throw new InvalidOperationException("The slot is absent.");

        public bool IsPresent(in RootMotionRequest value) => value.IsValid;

        public bool IsPresent(in RootMotionSlotAuthor value) => value.Present;
    }

    private readonly struct Source1 :
        IComponentValueReader<RootMotionSource, Guid>, IComponentValueReader<RootMotionSourceAuthor, Guid>
    {
        public Guid Get(in RootMotionSource value) => value.ClipSlotId;

        public Guid Get(in RootMotionSourceAuthor value) => value.ClipSlotId;
    }

    private readonly struct Source2 :
        IComponentValueReader<RootMotionSource, WorldAssetReference>, IComponentValueReader<RootMotionSourceAuthor, WorldAssetReference>
    {
        public WorldAssetReference Get(in RootMotionSource value) => new(value.Clip.Value, 1);

        public WorldAssetReference Get(in RootMotionSourceAuthor value) => value.Clip;
    }

    private readonly struct Source3 :
        IComponentValueReader<RootMotionSource, long>, IComponentValueReader<RootMotionSourceAuthor, long>
    {
        public long Get(in RootMotionSource value) => value.PlaySequence;

        public long Get(in RootMotionSourceAuthor value) => value.PlaySequence;
    }

    private readonly struct Source4 :
        IComponentValueReader<RootMotionSource, long>, IComponentValueReader<RootMotionSourceAuthor, long>
    {
        public long Get(in RootMotionSource value) => value.LoopOrdinal;

        public long Get(in RootMotionSourceAuthor value) => value.LoopOrdinal;
    }

    private readonly struct Source5 :
        IComponentValueReader<RootMotionSource, float>, IComponentValueReader<RootMotionSourceAuthor, float>
    {
        public float Get(in RootMotionSource value) => value.Weight;

        public float Get(in RootMotionSourceAuthor value) => value.Weight;
    }

    private readonly struct Values : IComponentArrayReader<RootMotionResourceAuthor, RootMotionSlotAuthor>
    {
        public int GetCount(in RootMotionResourceAuthor value) => value.Values.Length;

        public RootMotionSlotAuthor GetElement(in RootMotionResourceAuthor value, int index) => value.Values[index];
    }
}
