// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace TerraFX.Entities;

public sealed partial class World
{
    private const int MaximumSubscriptions = 64;
    private const string SubscriberThrowStackContextKey = "TerraFX.Entities.SubscriberThrowStack.v1";
    private const string SubscriptionCapacityMessage = "World subscription capacity (64) reached; subscription was not admitted.";

    private int _isPublicationDeliveryActive;
    private PublicationDeliveryReport? _lastDeliveryReport;
    private long _nextSubscriptionId;
    private int _subscriptionCount;
    private readonly long _subscriptionStorageByteReservation;
    private readonly WorldSubscriptionEntry[] _subscriptions;

    private static long CalculateSubscriptionStorageByteReservation()
    {
        var registrationBytes = ArrayBytes(MaximumSubscriptions, IntPtr.Size + sizeof(long));
        var capturedBytes = ArrayBytes(MaximumSubscriptions, IntPtr.Size + sizeof(long));
        var resultBytes = ArrayBytes(MaximumSubscriptions, IntPtr.Size);
        var failureBytes = ArrayBytes(MaximumSubscriptions, IntPtr.Size);
        var callbackControlBytes = checked((long)MaximumSubscriptions * 64L);
        var resultControlBytes = checked((long)MaximumSubscriptions * 128L);
        var diagnosticControlBytes = 512L;
        return checked(
            registrationBytes
            + capturedBytes
            + resultBytes
            + failureBytes
            + callbackControlBytes
            + resultControlBytes
            + diagnosticControlBytes);
    }

    /// <summary>Registers a callback for future committed World publications.</summary>
    /// <param name="callback">The callback invoked once for each captured publication.</param>
    /// <param name="subscription">The issued registration, or null when capacity is exhausted.</param>
    /// <returns><see langword="false"/> only when all 64 subscription slots are occupied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="callback"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The World is unavailable, the caller is not its owner, or the subscription identity is exhausted.</exception>
    public bool TrySubscribe(
        Action<CommittedWorldPublication> callback,
        [NotNullWhen(true)] out WorldSubscription? subscription)
    {
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();
        ThrowIfDisposed();
        ThrowIfFaulted();
        EnsureOwnerThread();

        if ((Volatile.Read(ref _isStructuralOperationActive) != 0)
            && (Volatile.Read(ref _isPublicationDeliveryActive) == 0))
        {
            throw new InvalidOperationException("World subscriptions are unavailable during a precommit operation.");
        }

        ArgumentNullException.ThrowIfNull(callback);
        var nextSubscriptionId = checked(_nextSubscriptionId + 1);

        if (_subscriptionCount == MaximumSubscriptions)
        {
            subscription = null;
            RecordSubscriptionCapacityWarning();
            return false;
        }

        var result = new WorldSubscription(this, nextSubscriptionId);
        _subscriptions[_subscriptionCount++] = new WorldSubscriptionEntry(nextSubscriptionId, callback);
        _nextSubscriptionId = nextSubscriptionId;
        subscription = result;
        return true;
    }

    internal void DisposeSubscription(WorldSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        EnsureOwnerThread();
        ThrowIfAttachmentCallbackActive();
        ThrowIfSystemInvocationActive();

        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!subscription.BelongsTo(this))
        {
            throw new ArgumentException("The subscription belongs to another World.", nameof(subscription));
        }

        if ((Volatile.Read(ref _isStructuralOperationActive) != 0)
            && (Volatile.Read(ref _isPublicationDeliveryActive) == 0))
        {
            throw new InvalidOperationException("World subscriptions are unavailable during a precommit operation.");
        }

        for (var index = 0; index < _subscriptionCount; index++)
        {
            if (_subscriptions[index].SubscriptionId != subscription.SubscriptionId)
            {
                continue;
            }

            for (var moveIndex = index + 1; moveIndex < _subscriptionCount; moveIndex++)
            {
                _subscriptions[moveIndex - 1] = _subscriptions[moveIndex];
            }

            _subscriptions[--_subscriptionCount] = default;
            return;
        }
    }

    private WorldSubscriptionEntry[] CaptureSubscriptions()
    {
        if (_subscriptionCount == 0)
        {
            return [];
        }

        var captured = new WorldSubscriptionEntry[_subscriptionCount];
        _subscriptions.AsSpan(0, _subscriptionCount).CopyTo(captured);
        return captured;
    }

    private void ClearSubscriptionsForDisposal()
    {
        Array.Clear(_subscriptions);
        _subscriptionCount = 0;
        _lastDeliveryReport = null;
    }

    private Diagnostic CreateSubscriberDeliveryDiagnostic(CommittedWorldPublication publication)
        => new(
            "SubscriberDeliveryFailed",
            DiagnosticSeverity.Error,
            _diagnosticsCapability.GetTimestamp(),
            null,
            _worldGuid,
            null,
            null,
            null,
            null,
            null,
            publication.CompletedTick,
            null,
            null,
            null,
            null,
            "TerraFX.Entities.World.Step",
            null,
            "A World publication subscriber failed.",
            [new DiagnosticContextEntry(SubscriberThrowStackContextKey, "U:Unqualified")]);

    [SuppressMessage("Usage", "CA1031:Do not catch general exception types", Justification = "A committed publication must attempt each captured subscriber once and preserve each exact ordinary callback failure in its immutable report.")]
    private void DeliverPublication(
        CommittedWorldPublication publication,
        WorldSubscriptionEntry[] capturedSubscriptions,
        PublicationDeliveryReport deliveryReport)
    {
        Volatile.Write(ref _isPublicationDeliveryActive, 1);

        try
        {
            for (var index = 0; index < capturedSubscriptions.Length; index++)
            {
                try
                {
                    capturedSubscriptions[index].Callback(publication);
                }
                catch (Exception exception)
                {
                    _diagnosticsCapability.Write(deliveryReport.GetFailureDiagnostic(index));
                    deliveryReport.Fail(index, exception);
                }
            }

            deliveryReport.Seal();
            Volatile.Write(ref _lastDeliveryReport, deliveryReport);
        }
        finally
        {
            Volatile.Write(ref _isPublicationDeliveryActive, 0);
        }
    }

    private void RecordSubscriptionCapacityWarning()
    {
        try
        {
            var diagnostic = new Diagnostic(
                "WorldSubscriptionCapacityExceeded",
                DiagnosticSeverity.Warning,
                _diagnosticsCapability.GetTimestamp(),
                null,
                _worldGuid,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "TerraFX.Entities.World.TrySubscribe",
                null,
                SubscriptionCapacityMessage,
                [
                    new DiagnosticContextEntry("ActiveSubscriptions", "64"),
                    new DiagnosticContextEntry("MaximumSubscriptions", "64"),
                ]);
            _diagnosticsCapability.Write(diagnostic);
        }
        catch (OutOfMemoryException)
        {
            _diagnosticsCapability.RecordDropped();
        }
    }

    private readonly struct WorldSubscriptionEntry
    {
        private readonly Action<CommittedWorldPublication> _callback;
        private readonly long _subscriptionId;

        internal WorldSubscriptionEntry(long subscriptionId, Action<CommittedWorldPublication> callback)
        {
            _subscriptionId = subscriptionId;
            _callback = callback;
        }

        internal Action<CommittedWorldPublication> Callback => _callback;

        internal long SubscriptionId => _subscriptionId;
    }
}
