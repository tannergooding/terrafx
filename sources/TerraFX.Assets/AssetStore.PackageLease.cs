// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Assets;

public sealed partial class AssetStore
{
    internal async Task<AssetLease<T>> LoadFromPackageAsync<T>(
        AssetId assetId,
        AssetPackageBacking backing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backing);
        AssetPackageEntry entry;
        AssetStoreRegistration.IAssetCodec<T> codec;
        long decodedBytes;

        try
        {
            if (!assetId.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(assetId));
            }

            cancellationToken.ThrowIfCancellationRequested();
            AcquireLoad<T>(
                assetId,
                backing,
                backingReferenceAlreadyHeld: true,
                out var loadBacking,
                out entry,
                out codec,
                out decodedBytes);
            backing = loadBacking;
        }
        catch
        {
            backing.ReleaseReference();
            throw;
        }

        return await LoadAssetAsync(assetId, backing, entry, codec, decodedBytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AssetLease<T>> LoadAssetAsync<T>(
        AssetId assetId,
        AssetPackageBacking backing,
        AssetPackageEntry entry,
        AssetStoreRegistration.IAssetCodec<T> codec,
        long decodedBytes,
        CancellationToken cancellationToken)
    {
        var leaseCreated = false;
        var decodeBudgetReserved = true;

        try
        {
            var encodedEnvelope = await AssetPayloadReader.ReadAsync(backing, entry, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var context = new AssetCodecContext(
                assetId,
                backing.Registration.Schemas,
                backing.ProfileId,
                backing.SpatialSettings,
                backing.ReadLimits);
            var status = AssetEnvelopeDecoder.Decode(
                encodedEnvelope,
                assetId,
                codec,
                context,
                backing.ReadLimits,
                out var value,
                out var assetReferences,
                out var schemaDiagnostic,
                out var packageDiagnostic);
            cancellationToken.ThrowIfCancellationRequested();

            if (status is not BinaryRecordStatus.Complete)
            {
                if (status is BinaryRecordStatus.UnsupportedSchema)
                {
                    throw new NotSupportedException(
                        $"Asset {assetId} uses an unsupported schema: {packageDiagnostic ?? schemaDiagnostic?.Category ?? status.ToString()}.");
                }

                throw new InvalidDataException(
                    $"Asset {assetId} was not decoded: {packageDiagnostic ?? schemaDiagnostic?.Category ?? status.ToString()}.");
            }

            if (value is null)
            {
                throw new InvalidDataException($"The codec returned no value for asset {assetId}.");
            }

            encodedEnvelope = [];
            ReleaseDecodeBudget(decodedBytes);
            decodeBudgetReserved = false;
            await ValidateAssetReferencesAsync(backing, assetReferences, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var lease = CreateLease(this, assetId, value, backing, decodedBytes, out leaseCreated);
            return lease;
        }
        finally
        {
            if (decodeBudgetReserved)
            {
                ReleaseDecodeBudget(decodedBytes);
            }

            CompleteLoad(decodedBytes, leaseCreated);

            if (!leaseCreated)
            {
                backing.ReleaseReference();
            }
        }
    }
}
