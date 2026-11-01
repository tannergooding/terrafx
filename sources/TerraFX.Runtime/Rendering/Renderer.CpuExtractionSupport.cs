// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Assets;
using TerraFX.Entities;

namespace TerraFX.Rendering;

/// <summary>Creates immutable CPU render snapshots from committed World state.</summary>
public sealed partial class Renderer
{
    private Renderer()
    {
    }

    /// <summary>Creates the opaque per-World CPU extraction registration.</summary>
    /// <returns>The registration to enlist before building a World registration.</returns>
    public static CpuExtractionSupportRegistration CreateCpuExtractionSupportRegistration()
        => new(new BuiltInCpuExtractionSupportRegistration());

    /// <summary>Creates a Renderer that performs CPU extraction without activating a graphics device.</summary>
    /// <returns>The headless Renderer service.</returns>
    public static Renderer CreateHeadless() => new();

    /// <summary>Registers the built-in native Texture, Atlas, and Material asset codecs with an Assets builder.</summary>
    /// <param name="builder">The asset-store builder whose schema registry contains the Rendering schemas.</param>
    /// <returns>The builder with the supported rendering asset codecs registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">The builder does not contain the exact compiled rendering schema tuples or registration conflicts.</exception>
    public static AssetStoreRegistrationBuilder RegisterAssetCodecs(AssetStoreRegistrationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.RegisterCodec(new RenderTextureAssetCodec());
        _ = builder.RegisterCodec(new RenderAtlasAssetCodec());
        return builder.RegisterCodec(new RenderMaterialAssetCodec());
    }

    private sealed class BuiltInCpuExtractionSupportRegistration : IWorldCpuExtractionSupportRegistration
    {
        private const long ObjectStorageByteReservation = 256;

        public IWorldCpuExtractionParticipant CreateParticipant(
            World world,
            WorldOptions options,
            IWorldSceneParticipant sceneParticipant)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(sceneParticipant);
            return new CpuExtractionWorldParticipant(
                world,
                sceneParticipant,
                GetWorldStorageByteReservation(options));
        }

        public long GetWorldStorageByteReservation(WorldOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return ObjectStorageByteReservation;
        }

        public void RegisterComponents(
            WorldRegistrationBuilder builder,
            CompiledContentSchemaRegistry schemas)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(schemas);
            _ = RenderComponentDefinitions.RegisterPointLight(builder, schemas);
            _ = RenderComponentDefinitions.RegisterDirectionalLight(builder, schemas);
            _ = RenderComponentDefinitions.RegisterSpotLight(builder, schemas);
        }
    }

    private sealed class CpuExtractionWorldParticipant : IWorldCpuExtractionParticipant
    {
        private int _isDisposed;
        private readonly long _ownedByteReservation;
        private readonly IWorldSceneParticipant _sceneParticipant;
        private readonly World _world;

        internal CpuExtractionWorldParticipant(
            World world,
            IWorldSceneParticipant sceneParticipant,
            long ownedByteReservation)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(sceneParticipant);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownedByteReservation);
            _world = world;
            _sceneParticipant = sceneParticipant;
            _ownedByteReservation = ownedByteReservation;
        }

        public long OwnedByteReservation => _ownedByteReservation;

        public WorldFrameReadLease AcquireFrameReadLease(IWorldSceneParticipant sceneParticipant)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

            if (!ReferenceEquals(sceneParticipant, _sceneParticipant))
            {
                throw new ArgumentException("The Scene participant is not the exact prerequisite captured by CPU extraction support.", nameof(sceneParticipant));
            }

            return _world.AcquireFrameReadLease(this, sceneParticipant);
        }

        public void Dispose()
        {
            _ = Interlocked.Exchange(ref _isDisposed, 1);
        }
    }
}
