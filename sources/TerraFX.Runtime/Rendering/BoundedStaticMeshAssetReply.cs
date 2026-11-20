// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Rendering;

internal readonly struct BoundedStaticMeshAssetReply
{
    internal BoundedStaticMeshAssetReply(
        BoundedStaticMeshAssetStatus status,
        BoundedStaticMeshAssetRefusal refusal,
        bool published,
        bool ownerReleased,
        bool consumersRemain,
        int retainedGenerationDebtCount,
        BoundedStaticMeshFailureFact primary,
        BoundedStaticMeshFailureFact candidateFile,
        BoundedStaticMeshFailureFact displacedOrFinalFile,
        BoundedStaticMeshFailureFact hashProvider,
        BoundedStaticMeshFailureFact ioEvent,
        BoundedStaticMeshFailureFact requestEvent,
        BoundedStaticMeshFailureFact replyEvent,
        BoundedStaticMeshFailureFact workerJoin)
    {
        Status = status;
        Refusal = refusal;
        Published = published;
        OwnerReleased = ownerReleased;
        ConsumersRemain = consumersRemain;
        RetainedGenerationDebtCount = retainedGenerationDebtCount;
        Primary = primary;
        CandidateFile = candidateFile;
        DisplacedOrFinalFile = displacedOrFinalFile;
        HashProvider = hashProvider;
        IoEvent = ioEvent;
        RequestEvent = requestEvent;
        ReplyEvent = replyEvent;
        WorkerJoin = workerJoin;
    }

    internal BoundedStaticMeshFailureFact CandidateFile { get; }

    internal bool ConsumersRemain { get; }

    internal BoundedStaticMeshFailureFact DisplacedOrFinalFile { get; }

    internal BoundedStaticMeshFailureFact HashProvider { get; }

    internal BoundedStaticMeshFailureFact IoEvent { get; }

    internal bool OwnerReleased { get; }

    internal BoundedStaticMeshFailureFact Primary { get; }

    internal bool Published { get; }

    internal BoundedStaticMeshAssetRefusal Refusal { get; }

    internal BoundedStaticMeshFailureFact ReplyEvent { get; }

    internal BoundedStaticMeshFailureFact RequestEvent { get; }

    internal int RetainedGenerationDebtCount { get; }

    internal BoundedStaticMeshAssetStatus Status { get; }

    internal BoundedStaticMeshFailureFact WorkerJoin { get; }
}
