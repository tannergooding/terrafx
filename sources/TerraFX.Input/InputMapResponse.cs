// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Input;

/// <summary>Reports one map publication result.</summary>
public sealed class InputMapResponse
{
    internal InputMapResponse(InputMapStatus status, long? hostSequence, long? publishedRevision, ValidationIssue? rejection)
    {
        Status = status;
        HostSequence = hostSequence;
        PublishedRevision = publishedRevision;
        Rejection = rejection;
    }

    /// <summary>Gets the sequence of the admitted host-control operation.</summary>
    public long? HostSequence { get; }

    /// <summary>Gets the published revision, or null when the candidate was not published.</summary>
    public long? PublishedRevision { get; }

    /// <summary>Gets the stable rejection reason, when publication failed.</summary>
    public ValidationIssue? Rejection { get; }

    /// <summary>Gets the publication outcome.</summary>
    public InputMapStatus Status { get; }
}
