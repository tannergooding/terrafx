// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Assets;

internal readonly struct AssetBoundedPreclaimRelease
{
    internal AssetBoundedPreclaimRelease(
        bool ownerReleased,
        AssetBoundedRefusal refusal,
        bool hasFailure,
        AssetBoundedFailureFact failure,
        long retainedSharedProvisionBytes)
    {
        OwnerReleased = ownerReleased;
        Refusal = refusal;
        HasFailure = hasFailure;
        Failure = failure;
        RetainedSharedProvisionBytes = retainedSharedProvisionBytes;
    }

    internal AssetBoundedFailureFact Failure { get; }

    internal bool HasFailure { get; }

    internal bool OwnerReleased { get; }

    internal AssetBoundedRefusal Refusal { get; }

    internal long RetainedSharedProvisionBytes { get; }
}
