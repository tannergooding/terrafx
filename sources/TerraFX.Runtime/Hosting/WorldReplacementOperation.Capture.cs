// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using TerraFX.Entities;

namespace TerraFX.Hosting;

internal sealed partial class WorldReplacementOperation
{
    internal void AbandonCapturedState()
    {
        var state = Interlocked.Exchange(ref _preparedState, null);
        var operationCredits = Interlocked.Exchange(ref _operationCredits, null);

        try
        {
            state?.Dispose();
        }
        finally
        {
            operationCredits?.Dispose();
        }
    }

    internal void EndLoadOperation()
    {
        if (Interlocked.Exchange(ref _isLoadEnded, 1) == 0)
        {
            _persistence.EndLoadOperation();
        }
    }

    internal WorldLoadReceipt RejectQueueFull()
    {
        AbandonCapturedState();
        return WorldPersistence.CreateLoadRejectedReceipt(
            "load-owner-queue-full",
            "The EngineHost owner queue is full; no replacement was published.",
            _plan.TargetWorldGuid);
    }

    internal bool TryCapture(out WorldLoadReceipt? rejection)
    {
        rejection = null;

        if (Interlocked.CompareExchange(ref _captureAttempted, 1, 0) != 0)
        {
            rejection = WorldPersistence.CreateLoadRejectedReceipt(
                "load-plan-unavailable",
                "The prepared World replacement is single-use.",
                _plan.TargetWorldGuid);
            return false;
        }

        WorldLoadPreparedState? preparedState = null;
        WorldDomainOperationCreditBundle? operationCredits = null;

        try
        {
            if (!_plan.TryCaptureCommit(
                    out preparedState,
                    out operationCredits,
                    out var bindingRefusal,
                    out var admissionRefusal))
            {
                rejection = (bindingRefusal.Code == WorldGuidRefusal.TargetBindingStale)
                    ? WorldPersistence.CreateLoadRejectedReceipt(
                        "load-target-binding-stale",
                        "The prepared plan or its exact target binding is disposed, already captured, stale, or unavailable.",
                        _plan.TargetWorldGuid)
                    : WorldPersistence.CreateLoadRejectedReceipt(
                        admissionRefusal == WorldAdmissionRefusal.DomainClosed
                            ? "load-domain-closed"
                            : "load-commit-capture-rejected",
                        $"The target World refused commit capture with admission code {admissionRefusal}.",
                        _plan.TargetWorldGuid);
                return false;
            }

            _preparedState = preparedState;
            _operationCredits = operationCredits;
            preparedState = null;
            operationCredits = null;

            if ((_preparedState is null) || (_operationCredits is null))
            {
                AbandonCapturedState();
                throw new InvalidOperationException("The captured World replacement omitted its state or operation credits.");
            }

            Volatile.Write(ref _isCaptured, 1);
            return true;
        }
        finally
        {
            preparedState?.Dispose();
            operationCredits?.Dispose();
        }
    }
}
