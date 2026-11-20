// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

internal readonly struct BoundedStaticMeshAssetPreclaimRelease
{
    internal BoundedStaticMeshAssetPreclaimRelease(
        bool ownerReleased,
        BoundedStaticMeshAssetRefusal refusal,
        bool hasFailure,
        BoundedStaticMeshFailureFact failure,
        long retainedSharedProvisionBytes)
    {
        OwnerReleased = ownerReleased;
        Refusal = refusal;
        HasFailure = hasFailure;
        Failure = failure;
        RetainedSharedProvisionBytes = retainedSharedProvisionBytes;
    }

    internal BoundedStaticMeshFailureFact Failure { get; }

    internal bool HasFailure { get; }

    internal bool OwnerReleased { get; }

    internal BoundedStaticMeshAssetRefusal Refusal { get; }

    internal long RetainedSharedProvisionBytes { get; }
}
