// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX;

internal sealed class FoundationCodecContext
{
    internal FoundationCodecContext(ImmutableArray<FoundationProfileBinding> profiles, FoundationPersistenceBinding? persistence = null, FoundationProfileTuple? packageProfile = null, int storageBudget = EbinCodec.MaximumRecordBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(storageBudget);

        if (profiles.IsDefaultOrEmpty)
        {
            ThrowHelper.ThrowArgumentException("A trusted profile selection is required.", nameof(profiles));
        }

        for (var index = 0; index < profiles.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(profiles[index]);

            for (var earlier = 0; earlier < index; earlier++)
            {
                if (profiles[index].Id == profiles[earlier].Id)
                {
                    ThrowHelper.ThrowArgumentException("A profile identity occurs twice in the selection.", nameof(profiles));
                }
            }
        }

        Profiles = profiles;
        Persistence = persistence;
        PackageProfile = packageProfile;
        StorageBudget = storageBudget;

        if (persistence is not null && !profiles.Contains(persistence.Profile))
        {
            ThrowHelper.ThrowArgumentException("The persistence profile must belong to the trusted selection.", nameof(persistence));
        }
    }

    internal FoundationProfileTuple? PackageProfile { get; }

    internal FoundationPersistenceBinding? Persistence { get; }

    internal ImmutableArray<FoundationProfileBinding> Profiles { get; }

    internal int StorageBudget { get; }

    internal FoundationProfileBinding? FindProfile(Guid id)
    {
        foreach (var profile in Profiles)
        {
            if (profile.Id.Value == id)
            {
                return profile;
            }
        }

        return null;
    }
}
