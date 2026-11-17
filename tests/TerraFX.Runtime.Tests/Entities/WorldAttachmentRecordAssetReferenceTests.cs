// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

internal sealed class WorldAttachmentRecordAssetReferenceTests
{
    private static WorldAttachmentRecordBinding<AssetRuntime, AssetAuthor> CreateBinding(
        out Guid targetSchemaId)
    {
        var assetReferenceAssignment = new ContentSchemaIdentityAssignment("native/asset-reference");
        var assetReferenceSchema = new ContentSchemaReference(
            assetReferenceAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var assetReferenceMetadata = new DataSchema(
            assetReferenceSchema,
            assetReferenceAssignment,
            ContentSchemaRole.Auxiliary,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [
                new ContentFieldDescriptor(
                    1,
                    3,
                    0,
                    true,
                    1,
                    1,
                    [],
                    0,
                    ContentUnit.None,
                    ContentReferenceKind.Asset,
                    ContentAuthoring.None,
                    null),
                new ContentFieldDescriptor(
                    2,
                    11,
                    0,
                    true,
                    1,
                    1,
                    [],
                    0,
                    ContentUnit.None,
                    ContentReferenceKind.Schema,
                    ContentAuthoring.None,
                    null),
                new ContentFieldDescriptor(
                    3,
                    3,
                    0,
                    true,
                    1,
                    1,
                    [],
                    0,
                    ContentUnit.None,
                    ContentReferenceKind.None,
                    ContentAuthoring.None,
                    null),
            ]);
        var targetAssignment = new ContentSchemaIdentityAssignment("native/aggregate-binding-test-asset");
        var targetSchema = new ContentSchemaReference(
            targetAssignment.SchemaId,
            1,
            0,
            ContentSchemaScope.Content);
        var targetMetadata = new DataSchema(
            targetSchema,
            targetAssignment,
            ContentSchemaRole.NativeAsset,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            []);
        var rootAssignment = new ContentSchemaIdentityAssignment("application/attachment-state/asset-reference-binding/v1");
        var rootSchema = new ContentSchemaReference(rootAssignment.SchemaId, 1, 0, ContentSchemaScope.Content);
        var assetField = new ContentFieldDescriptor(
            1,
            22,
            0,
            true,
            1,
            1,
            [assetReferenceSchema],
            0,
            ContentUnit.None,
            ContentReferenceKind.Asset,
            ContentAuthoring.None,
            null);
        var rootMetadata = new DataSchema(
            rootSchema,
            rootAssignment,
            ContentSchemaRole.AttachmentState,
            ContentSchemaOwner.Application,
            ContentSchemaStorage.NotComponent,
            ContentTransientRestore.NotApplicable,
            [assetField]);
        var schemas = new CompiledContentSchemaRegistry([
            assetReferenceMetadata,
            targetMetadata,
            rootMetadata,
        ]);
        var fieldBinding = WorldAttachmentRecordFieldBinding<AssetRuntime, AssetAuthor>.AssetReference<
            AssetRuntimeReader,
            AssetAuthorReader>(
                assetField,
                targetSchema.SchemaId,
                [1],
                default,
                default);
        targetSchemaId = targetSchema.SchemaId;
        return WorldAttachmentRecordBinding<AssetRuntime, AssetAuthor>.Create(
            schemas,
            rootSchema,
            [fieldBinding]);
    }

    [Test]
    public void AssetReferenceBindingPreservesTheTargetMajorPolicy()
    {
        var binding = CreateBinding(out var targetSchemaId);
        AssetAuthor major1 = new(new WorldAssetReference(assetId: 37, schemaMajor: 1));
        AssetRuntime runtimeMajor1 = new(new WorldAssetReference(assetId: 37, schemaMajor: 1));
        AssetAuthor major2 = new(new WorldAssetReference(assetId: 37, schemaMajor: 2));

        binding.ValidateAuthor(in major1);

        Assert.Multiple(() => {
            Assert.That(targetSchemaId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(runtimeMajor1.AssetReference.AssetId, Is.EqualTo(37));
            Assert.That(runtimeMajor1.AssetReference.SchemaMajor, Is.EqualTo(1));
            _ = Assert.Throws<InvalidDataException>(() => binding.ValidateAuthor(in major2));
        });
    }

    private sealed class AssetAuthor
    {
        internal AssetAuthor(WorldAssetReference assetReference)
        {
            AssetReference = assetReference;
        }

        internal WorldAssetReference AssetReference { get; }
    }

    private readonly struct AssetAuthorReader : IComponentValueReader<AssetAuthor, WorldAssetReference>
    {
        public WorldAssetReference Get(in AssetAuthor value) => value.AssetReference;
    }

    private sealed class AssetRuntime
    {
        internal AssetRuntime(WorldAssetReference assetReference)
        {
            AssetReference = assetReference;
        }

        internal WorldAssetReference AssetReference { get; }
    }

    private readonly struct AssetRuntimeReader : IComponentValueReader<AssetRuntime, WorldAssetReference>
    {
        public WorldAssetReference Get(in AssetRuntime value) => value.AssetReference;
    }
}
