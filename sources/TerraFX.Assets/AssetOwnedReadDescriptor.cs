// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Assets;

internal readonly struct AssetOwnedReadDescriptor
{
    internal AssetOwnedReadDescriptor(
        AssetStore store,
        AssetPackageBacking backing,
        AssetPackageEntry entry,
        AssetCodecRegistrationReceipt codecReceipt)
    {
        AssetId = entry.AssetId;
        BackingToken = backing.BackingToken;
        CodecReceipt = codecReceipt;
        CpuAssetBudgetBytes = backing.Options.CpuAssetBudgetBytes;
        DecodedLength = entry.DecodedLength;
        EntryCodec = entry.Codec;
        MaximumConcurrentDecodeBytes = backing.Options.MaximumConcurrentDecodeBytes;
        MaximumDecodedAssetBytes = backing.Options.MaximumDecodedAssetBytes;
        MaximumLiveAssetLeases = backing.Options.MaximumLiveAssetLeases;
        PackageByteLength = backing.PackageByteLength;
        ProfileId = backing.ProfileId;
        ReadLimits = backing.ReadLimits;
        Registration = backing.Registration;
        RequiredPackageCodec = codecReceipt.RequiredPackageCodec;
        Schemas = backing.Registration.Schemas;
        SpatialSettings = backing.SpatialSettings;
        Store = store;
        StoredLength = entry.StoredLength;
    }

    internal AssetId AssetId { get; }

    internal AssetPackageBackingToken BackingToken { get; }

    internal AssetCodecRegistrationReceipt CodecReceipt { get; }

    internal long CpuAssetBudgetBytes { get; }

    internal ulong DecodedLength { get; }

    internal byte EntryCodec { get; }

    internal int MaximumConcurrentDecodeBytes { get; }

    internal int MaximumDecodedAssetBytes { get; }

    internal int MaximumLiveAssetLeases { get; }

    internal long PackageByteLength { get; }

    internal SpatialProfileId ProfileId { get; }

    internal EbinReadLimits ReadLimits { get; }

    internal AssetStoreRegistration Registration { get; }

    internal AssetPackagePayloadCodec? RequiredPackageCodec { get; }

    internal CompiledContentSchemaRegistry Schemas { get; }

    internal SpatialSettings SpatialSettings { get; }

    internal AssetStore Store { get; }

    internal ulong StoredLength { get; }

    internal bool Matches(
        AssetStore store,
        AssetPackageBacking backing,
        AssetPackageEntry entry,
        AssetStoreRegistration.IAssetCodec codec)
        => ReferenceEquals(Store, store)
            && ReferenceEquals(BackingToken, backing.BackingToken)
            && (AssetId == entry.AssetId)
            && (EntryCodec == entry.Codec)
            && (StoredLength == entry.StoredLength)
            && (DecodedLength == entry.DecodedLength)
            && CodecReceipt.IsFor(codec)
            && (RequiredPackageCodec == codec.RequiredPackageCodec)
            && (RequiredPackageCodec is not AssetPackagePayloadCodec requiredCodec
                || EntryCodec == (byte)requiredCodec)
            && (CpuAssetBudgetBytes == backing.Options.CpuAssetBudgetBytes)
            && (MaximumConcurrentDecodeBytes == backing.Options.MaximumConcurrentDecodeBytes)
            && (MaximumDecodedAssetBytes == backing.Options.MaximumDecodedAssetBytes)
            && (MaximumLiveAssetLeases == backing.Options.MaximumLiveAssetLeases)
            && (PackageByteLength == backing.PackageByteLength)
            && ProfileId.Equals(backing.ProfileId)
            && ReferenceEquals(ReadLimits, backing.ReadLimits)
            && ReferenceEquals(Registration, backing.Registration)
            && ReferenceEquals(Schemas, backing.Registration.Schemas)
            && SpatialSettings.Equals(backing.SpatialSettings);
}
