// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Assets;

internal readonly struct AssetBoundedReply
{
    internal AssetBoundedReply(
        AssetBoundedStatus status,
        AssetBoundedRefusal refusal,
        bool published,
        bool ownerReleased,
        bool consumersRemain,
        int retainedGenerationDebtCount,
        AssetBoundedFailureFact primary,
        AssetBoundedFailureFact candidateFile,
        AssetBoundedFailureFact displacedOrFinalFile,
        AssetBoundedFailureFact hashProvider,
        AssetBoundedFailureFact ioEvent,
        AssetBoundedFailureFact requestEvent,
        AssetBoundedFailureFact replyEvent,
        AssetBoundedFailureFact workerJoin)
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

    internal AssetBoundedFailureFact CandidateFile { get; }

    internal bool ConsumersRemain { get; }

    internal AssetBoundedFailureFact DisplacedOrFinalFile { get; }

    internal AssetBoundedFailureFact HashProvider { get; }

    internal AssetBoundedFailureFact IoEvent { get; }

    internal bool OwnerReleased { get; }

    internal AssetBoundedFailureFact Primary { get; }

    internal bool Published { get; }

    internal AssetBoundedRefusal Refusal { get; }

    internal AssetBoundedFailureFact ReplyEvent { get; }

    internal AssetBoundedFailureFact RequestEvent { get; }

    internal int RetainedGenerationDebtCount { get; }

    internal AssetBoundedStatus Status { get; }

    internal AssetBoundedFailureFact WorkerJoin { get; }
}
