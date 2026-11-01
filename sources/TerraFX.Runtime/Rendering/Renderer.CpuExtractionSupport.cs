// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
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
