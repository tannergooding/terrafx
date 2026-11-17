// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

public sealed partial class GraphicsDevice
{
    private static void ClearRecordPassInputs(
        ref RenderPassCallback? callback,
        ref ImmutableArray<GraphResourceAccess> declaredAccesses,
        ref ImmutableArray<TextureFormat> colorTargetFormats,
        ref SkinnedNormalDiagnosticRecordingRecipe? diagnosticRecipe,
        bool releaseRecipe)
    {
        var recipe = diagnosticRecipe;
        diagnosticRecipe = null;
        callback = null;
        declaredAccesses = [];
        colorTargetFormats = [];

        if (releaseRecipe)
        {
            recipe?.Release();
        }
    }

    [DoesNotReturn]
    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "A diagnostic queue handoff that cannot fit immediately refuses the whole unsubmitted graph.")]
    private static void ThrowDiagnosticQueueUnavailable()
        => throw new OutOfMemoryException("The diagnostic owner-queue backing or immediate admission is unavailable.");

    internal async Task<RecordedPassCommandList> RecordPassAsync(
        RenderPassId passId,
        CommandRecorderPassKind passKind,
        ImmutableArray<GraphResourceAccess> declaredAccesses,
        ImmutableArray<TextureFormat> colorTargetFormats,
        TextureFormat? depthTargetFormat,
        RenderPassCallback? callback,
        CommandRecorderRecordingCapacity recordingCapacity = default,
        SkinnedNormalDiagnosticRecordingRecipe? diagnosticRecipe = null,
        bool requiresDiagnosticQueueAdmission = false)
    {
        RecordPassRequest? request = null;
        var queued = false;

        try
        {
            ArgumentNullException.ThrowIfNull(callback);
            CommandRecorderState.ValidatePassInputs(
                this, passId, passKind, declaredAccesses, colorTargetFormats, depthTargetFormat, requireOwnerThread: false);
            recordingCapacity.ValidateKnownBackingArrayMinimum();
            requiresDiagnosticQueueAdmission |= diagnosticRecipe is not null;
            request = new RecordPassRequest(
                this,
                passId,
                passKind,
                declaredAccesses,
                colorTargetFormats,
                depthTargetFormat,
                callback,
                recordingCapacity,
                diagnosticRecipe);
            ClearRecordPassInputs(ref callback, ref declaredAccesses, ref colorTargetFormats, ref diagnosticRecipe, releaseRecipe: false);

            if (requiresDiagnosticQueueAdmission)
            {
                if (!_ownerQueue.TryWriteDiagnosticRequest(request, GraphicsOwnerRequestQueue.KnownRequestBackingByteCharge))
                {
                    ThrowDiagnosticQueueUnavailable();
                }
            }
            else
            {
                await _ownerQueue.WriteRequestAsync(request, CancellationToken.None).ConfigureAwait(false);
            }

            queued = true;
            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            if (!queued)
            {
                request?.Abandon();
            }

            ClearRecordPassInputs(ref callback, ref declaredAccesses, ref colorTargetFormats, ref diagnosticRecipe, releaseRecipe: !queued);
        }
    }

    private sealed class RecordPassRequest : IGraphicsDeviceWork
    {
        private RenderPassCallback? _callback;
        private ImmutableArray<TextureFormat> _colorTargetFormats;
        private readonly TaskCompletionSource<RecordedPassCommandList> _completion;
        private ImmutableArray<GraphResourceAccess> _declaredAccesses;
        private readonly TextureFormat? _depthTargetFormat;
        private readonly GraphicsDevice _device;
        private SkinnedNormalDiagnosticRecordingRecipe? _diagnosticRecipe;
        private readonly RenderPassId _passId;
        private readonly CommandRecorderPassKind _passKind;
        private readonly CommandRecorderRecordingCapacity _recordingCapacity;

        internal RecordPassRequest(
            GraphicsDevice device,
            RenderPassId passId,
            CommandRecorderPassKind passKind,
            ImmutableArray<GraphResourceAccess> declaredAccesses,
            ImmutableArray<TextureFormat> colorTargetFormats,
            TextureFormat? depthTargetFormat,
            RenderPassCallback callback,
            CommandRecorderRecordingCapacity recordingCapacity,
            SkinnedNormalDiagnosticRecordingRecipe? diagnosticRecipe)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(callback);

            _callback = callback;
            _colorTargetFormats = colorTargetFormats;
            _declaredAccesses = declaredAccesses;
            _diagnosticRecipe = diagnosticRecipe;
            _device = device;
            _depthTargetFormat = depthTargetFormat;
            _passId = passId;
            _passKind = passKind;
            _recordingCapacity = recordingCapacity;
            _completion = new TaskCompletionSource<RecordedPassCommandList>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal Task<RecordedPassCommandList> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            RecordedPassCommandList? commandList = null;

            try
            {
                commandList = RecordOnOwner(device);
                ClearInputs(releaseRecipe: false);

                if (_completion.TrySetResult(commandList))
                {
                    commandList = null;
                }
            }
            finally
            {
                commandList?.Dispose();
                ClearInputs(releaseRecipe: true);
            }
        }

        public void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            Abandon();
            _ = _completion.TrySetException(exception);
        }

        internal void Abandon()
            => ClearInputs(releaseRecipe: true);

        private void ClearInputs(bool releaseRecipe)
            => ClearRecordPassInputs(ref _callback, ref _declaredAccesses, ref _colorTargetFormats, ref _diagnosticRecipe, releaseRecipe);

        // End recorder/borrowed-callback stack roots before publishing the completed Task.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private RecordedPassCommandList RecordOnOwner(GraphicsDevice device)
        {
            if (!ReferenceEquals(device, _device))
            {
                throw new InvalidOperationException("The recording request was delivered to a different GraphicsDevice.");
            }

            using var state = new CommandRecorderState(
                device,
                _passId,
                _passKind,
                _declaredAccesses,
                _colorTargetFormats,
                _depthTargetFormat,
                _recordingCapacity,
                _diagnosticRecipe);
            var recorder = new CommandRecorder(state);
            (_callback ?? throw new InvalidOperationException("The recording request lost its callback."))(ref recorder);
            return state.Complete();
        }
    }
}
