// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;

namespace TerraFX;

/// <summary>Provides the thread-safe, append-only registry of baseline spatial declarations.</summary>
public static class SpatialProfiles
{
    private const int Capacity = 4096;
    private static readonly Guid s_previousProfileId = new Guid("2ed8b750-e1a0-4b46-9000-000000000002");
    private static ReentrantGate s_lock;
    private static readonly Dictionary<SpatialProfileId, SpatialSettings> s_settings = [];

    static SpatialProfiles()
    {
        s_settings.Add(SpatialProfileId.Default, SpatialSettings.Default);
    }

    /// <summary>Registers a baseline declaration, or accepts an identical existing declaration.</summary>
    /// <param name="id">The nonempty profile identity.</param>
    /// <param name="settings">The valid unit and chunk declaration.</param>
    /// <exception cref="ArgumentException">An argument is invalid or the identity has a conflicting declaration or legacy manifest.</exception>
    /// <exception cref="InvalidOperationException">All 4096 registry identities are occupied.</exception>
    public static void Register(SpatialProfileId id, SpatialSettings settings)
    {
        SpatialKernel.ValidateId(id);
        SpatialKernel.ValidateSettings(settings);

        if (id.Value == s_previousProfileId)
        {
            ThrowHelper.ThrowArgumentException("The FloatChunk32V1 identity cannot be rebound to the FloatChunk32V2 baseline manifest.", nameof(id));
        }

        using (ReentrantGate.EnterScope(ref s_lock))
        {
            if (s_settings.TryGetValue(id, out var existing))
            {
                if (existing != settings)
                {
                    ThrowHelper.ThrowArgumentException("The spatial profile identity already has a different declaration.", nameof(settings));
                }

                return;
            }

            if (s_settings.Count == Capacity)
            {
                ThrowHelper.ThrowInvalidOperationException($"Spatial profile {id} cannot be registered: the {Capacity}-identity registry is full.");
            }

            s_settings.Add(id, settings);
        }
    }

    /// <summary>Looks up a valid identity; an unregistered identity returns false with default settings.</summary>
    /// <param name="id">The nonempty profile identity.</param>
    /// <param name="settings">The registered declaration, or an unusable default on false.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is invalid.</exception>
    public static bool TryGetSettings(SpatialProfileId id, out SpatialSettings settings)
    {
        SpatialKernel.ValidateId(id);
        using (ReentrantGate.EnterScope(ref s_lock))
        {
            return s_settings.TryGetValue(id, out settings);
        }
    }
}
