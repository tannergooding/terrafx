// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace TerraFX;

// Literal M02FoundationDescriptors1/M02FoundationKernel1 inputs from chapter 10 P2-P4.
// External manifests can describe this closure, but can never change its roots.
internal static class FoundationCatalogue
{
    internal static Guid RegistryManifestId { get; } = new Guid("a842b722-20a3-8e3e-9824-39b812becd31");
    internal static Guid RegistrySchemaId { get; } = new Guid("07902135-1633-8b5e-86e4-436850ca1149");
    internal static Guid RegistryFieldId { get; } = new Guid("4ec03903-a0b1-8425-8f43-0e905f7f0764");
    internal static Guid ProfileId { get; } = new Guid("3b9760ce-468a-8aab-bb89-f38db9e3bc00");
    internal static Guid Frame2Id { get; } = new Guid("ae928678-285e-82be-b250-cea4bb2cb7c3");
    internal static Guid Frame3Id { get; } = new Guid("42626aff-fc5c-864d-867c-43c666699777");
    internal static Guid KernelManifestId { get; } = new Guid("b4bbf3fb-503c-8a6d-8687-878cbc3cc9d6");
    internal static Guid KernelRecipeId { get; } = new Guid("f7a613f1-1c19-8c64-9448-31be7d694db2");
    internal static Guid LegacyProfileId { get; } = new Guid("2ed8b750-e1a0-4b46-9000-000000000002");

    internal static ImmutableArray<FoundationSchemaDescriptor> Schemas { get; } = [new FoundationSchemaDescriptor(RegistryManifestId, 1, [new FoundationFieldDescriptor(1, 24, 1, 1_048_576)]), new FoundationSchemaDescriptor(RegistrySchemaId, 1, [new FoundationFieldDescriptor(1, 11, 1, 1), new FoundationFieldDescriptor(2, 3, 1, 1), new FoundationFieldDescriptor(3, 3, 1, 1), new FoundationFieldDescriptor(4, 24, 1, 1_048_576)]), new FoundationSchemaDescriptor(RegistryFieldId, 1, [new FoundationFieldDescriptor(1, 3, 1, 1), new FoundationFieldDescriptor(2, 2, 1, 1), new FoundationFieldDescriptor(3, 9, 1, 1), new FoundationFieldDescriptor(4, 4, 1, 1), new FoundationFieldDescriptor(5, 4, 1, 1)]), new FoundationSchemaDescriptor(ProfileId, 2, [new FoundationFieldDescriptor(1, 3, 1, 1), new FoundationFieldDescriptor(2, 3, 1, 1), new FoundationFieldDescriptor(3, 3, 1, 1), new FoundationFieldDescriptor(5, 1, 1, 1), new FoundationFieldDescriptor(6, 13, 32, 32), new FoundationFieldDescriptor(7, 13, 32, 32), new FoundationFieldDescriptor(8, 13, 32, 32), new FoundationFieldDescriptor(9, 11, 1, 1), new FoundationFieldDescriptor(10, 7, 1, 1)]), new FoundationSchemaDescriptor(Frame2Id, 1, [new FoundationFieldDescriptor(1, 11, 1, 1), new FoundationFieldDescriptor(2, 22, 1, 1), new FoundationFieldDescriptor(3, 20, 1, 1), new FoundationFieldDescriptor(4, 4, 1, 1), new FoundationFieldDescriptor(5, 14, 1, 1), new FoundationFieldDescriptor(6, 14, 1, 1)]), new FoundationSchemaDescriptor(Frame3Id, 1, [new FoundationFieldDescriptor(1, 11, 1, 1), new FoundationFieldDescriptor(2, 22, 1, 1), new FoundationFieldDescriptor(3, 21, 1, 1), new FoundationFieldDescriptor(4, 4, 1, 1), new FoundationFieldDescriptor(5, 15, 1, 1), new FoundationFieldDescriptor(6, 15, 1, 1)]), new FoundationSchemaDescriptor(KernelManifestId, 1, [new FoundationFieldDescriptor(1, 3, 1, 1), new FoundationFieldDescriptor(2, 3, 1, 1), new FoundationFieldDescriptor(3, 3, 1, 1), new FoundationFieldDescriptor(4, 24, 1, 1_048_576)]), new FoundationSchemaDescriptor(KernelRecipeId, 1, [new FoundationFieldDescriptor(1, 11, 1, 1), new FoundationFieldDescriptor(2, 3, 1, 1), new FoundationFieldDescriptor(3, 13, 0, 1024)])];

    internal static ImmutableArray<FoundationRecipe> Recipes { get; } = [new FoundationRecipe(new Guid("93ec20f5-b161-8d79-aafb-8ec1438c794f"), "0000"), new FoundationRecipe(new Guid("36bf491c-f7b6-88fe-a931-31b03a8b40cf"), ""), new FoundationRecipe(new Guid("fe722d22-1099-8438-8531-eb330c3b3589"), "0100000020a10700"), new FoundationRecipe(new Guid("c8af393c-d701-8332-bee5-ab6cb228a60e"), "2c02aa010004"), new FoundationRecipe(new Guid("dab554c7-5762-8b00-9ba3-910a68157c96"), "a086010020a10700"), new FoundationRecipe(new Guid("408ce437-ab29-85de-93b6-6ef24c8ea1e3"), "0100000020a10700"), new FoundationRecipe(new Guid("fbfbcfda-0497-8115-8e79-ed024525aa80"), "00000080ffffff7f01"), new FoundationRecipe(new Guid("aa5cd1e7-05a4-8630-b91b-bf83420da8f6"), "000403e2ffffff1e000000"), new FoundationRecipe(new Guid("fbe9d96e-0119-8d57-b9ff-903e3b0fcc2c"), "4000"),];

    internal static EbinRecord Registry { get; } = BuildRegistry();
    internal static EbinRecord Kernel { get; } = BuildKernel();
    private static readonly byte[] s_registryBytes = EbinPrimitives.Encode(Registry);
    private static readonly byte[] s_kernelBytes = EbinPrimitives.Encode(Kernel);
    private static readonly byte[] s_registryDigest = SHA256.HashData(s_registryBytes);
    private static readonly byte[] s_kernelDigest = SHA256.HashData(s_kernelBytes);
    internal static ReadOnlySpan<byte> KernelBytes => s_kernelBytes;
    internal static ReadOnlySpan<byte> KernelDigest => s_kernelDigest;

    internal static ReadOnlySpan<byte> RegistryBytes => s_registryBytes;
    internal static ReadOnlySpan<byte> RegistryDigest => s_registryDigest;

    internal static int CompareRfc(Guid left, Guid right)
    {
        Unsafe.SkipInit(out InlineArray16<byte> aStorage); // TryWriteBytes fills all 16 bytes before comparison.
        Unsafe.SkipInit(out InlineArray16<byte> bStorage); // TryWriteBytes fills all 16 bytes before comparison.
        Span<byte> a = aStorage;
        Span<byte> b = bStorage;

        _ = left.TryWriteBytes(a, bigEndian: true, out _);
        _ = right.TryWriteBytes(b, bigEndian: true, out _);
        return a.SequenceCompareTo(b);
    }

    internal static FoundationSchemaDescriptor? Find(Guid id)
    {
        foreach (var schema in Schemas)
        {
            if (schema.Id == id)
            {
                return schema;
            }
        }

        return null;
    }

    internal static FoundationRecipe? FindRecipe(Guid id)
    {
        foreach (var recipe in Recipes)
        {
            if (recipe.Id == id)
            {
                return recipe;
            }
        }

        return null;
    }

    private static EbinRecord BuildKernel()
    {
        var recipes = Recipes.AsSpan().ToArray();
        Array.Sort(recipes, static (left, right) => CompareRfc(left.Id, right.Id));

        var records = new EbinRecord[recipes.Length];

        for (var index = 0; index < records.Length; index++)
        {
            records[index] = new EbinRecord(KernelRecipeId, 1, 0, [
                EbinPrimitives.Guid(1, recipes[index].Id), EbinPrimitives.UInt32(2, 1),
                EbinPrimitives.Bytes(3, recipes[index].Parameters),
            ]);
        }

        return new EbinRecord(KernelManifestId, 1, 0, [
            EbinPrimitives.UInt32(1, 1), EbinPrimitives.UInt32(2, 2),
            EbinPrimitives.UInt32(3, 2), EbinPrimitives.RecordArray(4, records),
        ]);
    }

    private static EbinRecord BuildRegistry()
    {
        var schemas = Schemas.AsSpan().ToArray();
        Array.Sort(schemas, static (left, right) => CompareRfc(left.Id, right.Id));

        var records = new EbinRecord[schemas.Length];

        for (var index = 0; index < schemas.Length; index++)
        {
            var schema = schemas[index];
            var fields = new EbinRecord[schema.Fields.Length];

            for (var fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                var field = schema.Fields[fieldIndex];
                fields[fieldIndex] = new EbinRecord(RegistryFieldId, 1, 0, [
                    EbinPrimitives.UInt32(1, field.Tag), EbinPrimitives.UInt16(2, field.Kind),
                    EbinPrimitives.Boolean(3, field.Required), EbinPrimitives.UInt64(4, field.Minimum),
                    EbinPrimitives.UInt64(5, field.Maximum),
                ]);
            }

            records[index] = new EbinRecord(RegistrySchemaId, 1, 0, [
                EbinPrimitives.Guid(1, schema.Id), EbinPrimitives.UInt32(2, schema.Major),
                EbinPrimitives.UInt32(3, 0), EbinPrimitives.RecordArray(4, fields),
            ]);
        }

        return new EbinRecord(RegistryManifestId, 1, 0, [EbinPrimitives.RecordArray(1, records)]);
    }
}
