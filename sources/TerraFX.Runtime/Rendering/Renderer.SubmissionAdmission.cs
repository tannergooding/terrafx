// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Rendering;

public sealed partial class Renderer
{
    private readonly Lock _submissionGate = new();
    private int _activeDiagnosticSubmission;
    private int _activeSubmissionCount;
    private bool _submissionAdmissionClosed;
    private TaskCompletionSource? _submissionDrain;

    internal Task StopAndDrainSubmissionsAsync()
    {
        lock (_submissionGate)
        {
            _submissionAdmissionClosed = true;

            if (_activeSubmissionCount == 0)
            {
                return Task.CompletedTask;
            }

            _submissionDrain ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _submissionDrain.Task;
        }
    }

    private SubmissionAdmissionLease AcquireSubmissionAdmission(bool diagnosticSubmission)
    {
        lock (_submissionGate)
        {
            ObjectDisposedException.ThrowIf(_submissionAdmissionClosed, this);

            if (diagnosticSubmission && (_activeDiagnosticSubmission != 0))
            {
                throw new InvalidOperationException("A skinned-normal diagnostic submission is already active for this Renderer.");
            }

            _activeSubmissionCount = checked(_activeSubmissionCount + 1);

            if (diagnosticSubmission)
            {
                _activeDiagnosticSubmission = 1;
            }

            return new SubmissionAdmissionLease(this, diagnosticSubmission);
        }
    }

    private void ReleaseSubmissionAdmission(bool diagnosticSubmission)
    {
        TaskCompletionSource? submissionDrain = null;

        lock (_submissionGate)
        {
            if (_activeSubmissionCount <= 0)
            {
                throw new InvalidOperationException("The Renderer released a submission admission more than once.");
            }

            _activeSubmissionCount--;

            if (diagnosticSubmission)
            {
                if (_activeDiagnosticSubmission == 0)
                {
                    throw new InvalidOperationException("The Renderer released an inactive diagnostic submission.");
                }

                _activeDiagnosticSubmission = 0;
            }

            if (_submissionAdmissionClosed && (_activeSubmissionCount == 0))
            {
                submissionDrain = _submissionDrain;
            }
        }

        _ = submissionDrain?.TrySetResult();
    }

    private readonly struct SubmissionAdmissionLease : IDisposable
    {
        private readonly bool _diagnosticSubmission;
        private readonly Renderer _renderer;

        internal SubmissionAdmissionLease(Renderer renderer, bool diagnosticSubmission)
        {
            ArgumentNullException.ThrowIfNull(renderer);
            _renderer = renderer;
            _diagnosticSubmission = diagnosticSubmission;
        }

        public void Dispose()
            => _renderer.ReleaseSubmissionAdmission(_diagnosticSubmission);
    }
}
