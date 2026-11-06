// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Graphics;

public sealed partial class GraphicsDevice
{
    internal async Task<RecordedPassCommandList> RecordPassAsync(
        RenderPassId passId,
        CommandRecorderPassKind passKind,
        ImmutableArray<GraphResourceAccess> declaredAccesses,
        ImmutableArray<TextureFormat> colorTargetFormats,
        TextureFormat? depthTargetFormat,
        RenderPassCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting recording requests.");
        }

        var request = new RecordPassRequest(
            this,
            passId,
            passKind,
            declaredAccesses,
            colorTargetFormats,
            depthTargetFormat,
            callback);

        await _ownerQueue.WriteRequestAsync(request, CancellationToken.None).ConfigureAwait(false);

        return await request.Task.ConfigureAwait(false);
    }

    private sealed class RecordPassRequest : IGraphicsDeviceWork
    {
        private readonly RenderPassCallback _callback;
        private readonly ImmutableArray<TextureFormat> _colorTargetFormats;
        private readonly TaskCompletionSource<RecordedPassCommandList> _completion;
        private readonly ImmutableArray<GraphResourceAccess> _declaredAccesses;
        private readonly TextureFormat? _depthTargetFormat;
        private readonly GraphicsDevice _device;
        private readonly RenderPassId _passId;
        private readonly CommandRecorderPassKind _passKind;

        internal RecordPassRequest(
            GraphicsDevice device,
            RenderPassId passId,
            CommandRecorderPassKind passKind,
            ImmutableArray<GraphResourceAccess> declaredAccesses,
            ImmutableArray<TextureFormat> colorTargetFormats,
            TextureFormat? depthTargetFormat,
            RenderPassCallback callback)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(callback);

            _callback = callback;
            _colorTargetFormats = colorTargetFormats;
            _declaredAccesses = declaredAccesses;
            _device = device;
            _depthTargetFormat = depthTargetFormat;
            _passId = passId;
            _passKind = passKind;
            _completion = new TaskCompletionSource<RecordedPassCommandList>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal Task<RecordedPassCommandList> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (!ReferenceEquals(device, _device))
            {
                throw new InvalidOperationException("The recording request was delivered to a different GraphicsDevice.");
            }

            var state = new CommandRecorderState(
                device,
                _passId,
                _passKind,
                _declaredAccesses,
                _colorTargetFormats,
                _depthTargetFormat);

            RecordedPassCommandList? commandList = null;

            try
            {
                var recorder = new CommandRecorder(state);
                _callback(ref recorder);
                commandList = state.Complete();

                if (_completion.TrySetResult(commandList))
                {
                    commandList = null;
                }
            }
            catch
            {
                state.Dispose();
                throw;
            }
            finally
            {
                commandList?.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _ = _completion.TrySetException(exception);
        }
    }
}
