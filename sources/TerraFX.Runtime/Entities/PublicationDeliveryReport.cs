// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;

namespace TerraFX.Entities;

/// <summary>Contains delivery outcomes after an immutable World publication committed.</summary>
public sealed class PublicationDeliveryReport
{
    private readonly SubscriberDeliveryFailure?[] _failureBuffer;
    private int _isSealed;
    private readonly CommittedWorldPublication _publication;
    private readonly SubscriberDeliveryResult[] _resultBuffer;
    private ImmutableArray<SubscriberDeliveryResult> _results;

    internal PublicationDeliveryReport(
        CommittedWorldPublication publication,
        SubscriberDeliveryResult[] resultBuffer,
        SubscriberDeliveryFailure?[] failureBuffer)
    {
        ArgumentNullException.ThrowIfNull(publication);
        ArgumentNullException.ThrowIfNull(resultBuffer);
        ArgumentNullException.ThrowIfNull(failureBuffer);

        if (resultBuffer.Length != failureBuffer.Length)
        {
            throw new ArgumentException("Delivery result and failure buffers must have matching lengths.", nameof(failureBuffer));
        }

        for (var index = 0; index < resultBuffer.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(resultBuffer[index]);
            ArgumentNullException.ThrowIfNull(failureBuffer[index]);
        }

        _publication = publication;
        _resultBuffer = resultBuffer;
        _failureBuffer = failureBuffer;
    }

    /// <summary>Gets the execution-session identity for operational delivery.</summary>
    public Guid ExecutionSessionId => _publication.ExecutionSessionId;

    /// <summary>Gets the exact committed publication delivered to subscribers.</summary>
    public CommittedWorldPublication Publication => _publication;

    /// <summary>Gets the publication sequence.</summary>
    public long PublicationSequence => _publication.PublicationSequence;

    /// <summary>Gets the immutable outcomes for the captured subscription set.</summary>
    public ImmutableArray<SubscriberDeliveryResult> Results
    {
        get
        {
            if (Volatile.Read(ref _isSealed) == 0)
            {
                throw new InvalidOperationException("The publication delivery report is not sealed.");
            }

            return _results;
        }
    }

    /// <summary>Gets the runtime World owner identity.</summary>
    public uint WorldOwnerId => _publication.WorldOwnerId;

    internal void Fail(int index, Exception exception)
    {
        if (Volatile.Read(ref _isSealed) != 0)
        {
            throw new InvalidOperationException("The publication delivery report is already sealed.");
        }

        ArgumentNullException.ThrowIfNull(exception);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _resultBuffer.Length);
        var failure = _failureBuffer[index]
            ?? throw new InvalidOperationException("The prepared subscriber failure is missing.");
        failure.Initialize(exception);
        _resultBuffer[index].Fail(failure);
    }

    internal Diagnostic GetFailureDiagnostic(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _failureBuffer.Length);
        return _failureBuffer[index]?.Diagnostic
            ?? throw new InvalidOperationException("The prepared subscriber failure diagnostic is missing.");
    }

    internal void Seal()
    {
        if (Volatile.Read(ref _isSealed) != 0)
        {
            throw new InvalidOperationException("The publication delivery report is already sealed.");
        }

        Array.Clear(_failureBuffer);
        _results = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(_resultBuffer);
        Volatile.Write(ref _isSealed, 1);
    }
}
