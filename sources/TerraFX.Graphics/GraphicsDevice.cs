// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Buffers;

namespace TerraFX.Graphics;

/// <summary>Owns one host-created native graphics device on its dedicated owner thread.</summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The event is released by the dedicated graphics owner thread after shutdown; callers control lifetime through ShutdownAsync.")]
public sealed class GraphicsDevice
{
    private const long DefaultOwnedAllocationBudgetBytes = 512L * 1024 * 1024;

    // Keep an owner rooted if native disposal cannot confirm that its resources were released.
    private static readonly ConcurrentBag<IGraphicsBackendDevice> s_quarantinedBackendDevices = [];
    private static readonly ConcurrentBag<IGraphicsBackendResource> s_quarantinedBackendResources = [];
    private static readonly ConcurrentBag<GraphicsDevice> s_quarantinedGraphicsDevices = [];
    private static readonly ConcurrentBag<GraphicsResourceOwner> s_quarantinedResources = [];

    private readonly Dictionary<int, long> _allocatedBytesByHeap = [];
    private long _allocatedResourceBytes;
    private IGraphicsBackendDevice? _backendDevice;
    private readonly GraphicsCapabilities _capabilities;
    private readonly HashSet<GraphicsResourceOwner> _deferredRetirements = [];
    private bool _hasQuarantinedResources;
    private ulong _nextBufferIndex;
    private ulong _nextPipelineIndex;
    private ulong _nextSamplerIndex;
    private ulong _nextTextureIndex;
    private readonly uint _ownerId;
    private readonly GraphicsOwnerRequestQueue _ownerQueue = new();
    private readonly Thread _ownerThread;
    private readonly HashSet<GraphicsResourceOwner> _resources = [];
    private Exception? _retirementFailure;
    private readonly TaskCompletionSource _shutdownCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _shutdownTask;
    private int _status;

    private GraphicsDevice(uint ownerId, IGraphicsBackendDevice backendDevice)
    {
        _ownerId = ownerId;
        _backendDevice = backendDevice;
        _capabilities = backendDevice.Capabilities;
        _ownerThread = Thread.CurrentThread;
        DeviceEpoch = 1;
        _status = (int)GraphicsDeviceStatus.Ready;
        _shutdownTask = WaitForShutdownAndOwnerExitAsync(_shutdownCompletion.Task, _ownerThread);
    }

    /// <summary>Gets the immutable capabilities reported by the selected backend.</summary>
    public GraphicsCapabilities Capabilities => _capabilities;

    /// <summary>Gets the device epoch, initially one for this host-issued owner.</summary>
    public uint DeviceEpoch { get; }

    /// <summary>Gets the current lifecycle state.</summary>
    public GraphicsDeviceStatus Status
        => (GraphicsDeviceStatus)Volatile.Read(ref _status);

    /// <summary>Gets the host-issued owner identity used by device resource handles.</summary>
    internal uint OwnerId => _ownerId;

    internal static Task<GraphicsDevice> CreateAsync(
        uint ownerId,
        GraphicsBackendRegistration registration,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentNullException.ThrowIfNull(registration);
        ValidateStartupProfile(registration.Backend, profile, platformKind);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<GraphicsDevice>(cancellationToken);
        }

        return new GraphicsDeviceStartup(
            ownerId,
            registration,
            profile,
            platformKind,
            cancellationToken).Start();
    }

    internal static (int Width, int Height, int Depth) GetTextureSubresourceExtent(
        TextureDescription description,
        TextureSubresource subresource)
    {
        if (!Enum.IsDefined(subresource.Aspect))
        {
            throw new ArgumentOutOfRangeException(nameof(subresource));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(subresource.MipLevel, description.MipLevels, nameof(subresource));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(subresource.ArrayLayer, description.ArrayLayers, nameof(subresource));

        if ((description.Dimension == TextureDimension.Dimension3D) && (subresource.ArrayLayer != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(subresource), "A 3D texture has array layer zero.");
        }

        var isDepthFormat = description.Format == TextureFormat.D32Float;

        if (isDepthFormat ? subresource.Aspect != TextureAspect.Depth : subresource.Aspect != TextureAspect.Color)
        {
            throw new ArgumentException("The selected aspect is incompatible with the texture format.", nameof(subresource));
        }

        return (
            GetTextureMipExtent(description.Width, subresource.MipLevel),
            GetTextureMipExtent(description.Height, subresource.MipLevel),
            description.Dimension == TextureDimension.Dimension2D
                ? 1
                : GetTextureMipExtent(description.Depth, subresource.MipLevel));
    }

    internal static void ValidateStartupProfile(
        GraphicsBackend backend,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind)
    {
        if ((backend == GraphicsBackend.None) || !Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }

        if ((profile == GraphicsDeviceProfile.None) || !Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        if (!Enum.IsDefined(platformKind))
        {
            throw new ArgumentOutOfRangeException(nameof(platformKind));
        }

        if (profile == GraphicsDeviceProfile.OffscreenNative)
        {
            if (platformKind != GraphicsPlatformKind.None)
            {
                throw new ArgumentException("OffscreenNative device creation must not select a native window platform.", nameof(platformKind));
            }

            return;
        }

        if (platformKind == GraphicsPlatformKind.None)
        {
            throw new ArgumentException("Interactive device creation requires an initialized native window platform.", nameof(platformKind));
        }

        if ((backend == GraphicsBackend.D3D12) && (platformKind != GraphicsPlatformKind.Win32))
        {
            throw new PlatformNotSupportedException("Interactive D3D12 device creation requires Win32.");
        }
    }

    internal static nint ValidateTextureUploadSource(
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout,
        int width,
        int height,
        int depth,
        TextureFormat format)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLayout.RowPitch, nameof(sourceLayout));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceLayout.SlicePitch, nameof(sourceLayout));
        var rowByteLength = checked((nint)width * TextureReadback.GetBytesPerTexel(format));
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceLayout.RowPitch, rowByteLength, nameof(sourceLayout));
        var minimumSlicePitch = checked((sourceLayout.RowPitch * (height - 1)) + rowByteLength);
        ArgumentOutOfRangeException.ThrowIfLessThan(sourceLayout.SlicePitch, minimumSlicePitch, nameof(sourceLayout));
        var sourceByteLength = checked((sourceLayout.SlicePitch * (depth - 1)) + minimumSlicePitch);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceOffset, source.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceByteLength, source.Length - sourceOffset, nameof(source));
        return sourceByteLength;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A work-item failure is delivered through that operation's task without terminating the dedicated owner loop.")]
    private static void ExecuteOwnerWork(IGraphicsDeviceWork work, GraphicsDevice device)
    {
        try
        {
            work.Execute(device);
        }
        catch (Exception exception)
        {
            work.Fail(exception);
        }
    }

    private static ImmutableArray<ShaderBytecodeInput> GetPipelineShaderInputs(PipelineDescription description)
        => description switch {
            GraphicsPipelineDescription graphicsDescription => graphicsDescription.Shaders,
            ComputePipelineDescription computeDescription => [computeDescription.Shader],
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        };

    private static int GetTextureMipExtent(int extent, int mipLevel)
        => Math.Max(extent >> mipLevel, 1);

    private static void ReleaseShaderBytecode(BufferReadLease<byte>[]? bytecodeLeases)
    {
        if (bytecodeLeases is null)
        {
            return;
        }

        for (var index = 0; index < bytecodeLeases.Length; index++)
        {
            bytecodeLeases[index].Dispose();
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned independent bytecode credits are transferred to the admitted pipeline request; partial acquisitions are released here.")]
    private static BufferReadLease<byte>[] RetainShaderBytecode(PipelineDescription description)
    {
        var shaderInputs = GetPipelineShaderInputs(description);
        var bytecodeLeases = new BufferReadLease<byte>[shaderInputs.Length];
        var retainedCount = 0;

        try
        {
            for (var index = 0; index < shaderInputs.Length; index++)
            {
                bytecodeLeases[index] = shaderInputs[index].Bytecode.Retain();
                retainedCount++;
            }

            return bytecodeLeases;
        }
        catch
        {
            for (var index = 0; index < retainedCount; index++)
            {
                bytecodeLeases[index].Dispose();
            }

            throw;
        }
    }

    private static async Task<UploadReceipt> UploadBufferAsyncCore(
        GraphicsDevice device,
        GpuBuffer destination,
        long destinationOffset,
        byte[]? sourceBytes,
        BufferReadLease<byte>? sourceLease,
        nint sourceOffset,
        nint byteLength,
        CancellationToken cancellationToken)
    {
        var sourceLeaseTransferred = false;
        GraphicsResourceOwner.UseLease? destinationUse = null;

        try
        {
            if (device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The GraphicsDevice is not accepting buffer uploads.");
            }

            destinationUse = destination.ResourceOwner.AcquireUse(nameof(destination));
            var request = new UploadBufferRequest(
                device,
                destination,
                destinationUse,
                destinationOffset,
                sourceBytes,
                sourceLease,
                sourceOffset,
                byteLength,
                cancellationToken);
            destinationUse = null;
            sourceLeaseTransferred = true;

            try
            {
                await device._ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                request.AdmissionFailed();
                throw;
            }

            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            if (!sourceLeaseTransferred)
            {
                sourceLease?.Dispose();
            }

            destinationUse?.Dispose();
        }
    }

    private static async Task<UploadReceipt> UploadTextureAsyncCore(
        GraphicsDevice device,
        GpuTexture destination,
        TextureSubresource subresource,
        BufferReadLease<byte> sourceLease,
        nint sourceOffset,
        TextureDataLayout sourceLayout,
        CancellationToken cancellationToken)
    {
        var sourceLeaseTransferred = false;
        GraphicsResourceOwner.UseLease? destinationUse = null;

        try
        {
            if (device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The GraphicsDevice is not accepting texture uploads.");
            }

            destinationUse = destination.ResourceOwner.AcquireUse(nameof(destination));
            var request = new UploadTextureRequest(
                device,
                destination,
                destinationUse,
                subresource,
                sourceLease,
                sourceOffset,
                sourceLayout,
                cancellationToken);
            destinationUse = null;
            sourceLeaseTransferred = true;

            try
            {
                await device._ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                request.AdmissionFailed();
                throw;
            }

            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            if (!sourceLeaseTransferred)
            {
                sourceLease.Dispose();
            }

            destinationUse?.Dispose();
        }
    }

    private static void ValidateBufferForTransfer(
        GraphicsDevice device,
        GpuBuffer buffer,
        BufferUsage requiredUsage,
        string parameterName,
        bool allowRetiring = false)
    {
        ArgumentNullException.ThrowIfNull(buffer, parameterName);

        if (!buffer.Handle.IsValid
            || (buffer.Handle.OwnerId != device.OwnerId)
            || (buffer.DeviceEpoch != device.DeviceEpoch)
            || !ReferenceEquals(buffer.ResourceOwner.Device, device))
        {
            throw new ArgumentException("The buffer must belong to this GraphicsDevice generation.", parameterName);
        }

        if (!allowRetiring && !buffer.ResourceOwner.IsLiveForNewWork)
        {
            throw new ObjectDisposedException(parameterName, "The buffer no longer accepts new transfer work.");
        }

        if ((buffer.Description.Usage & requiredUsage) != requiredUsage)
        {
            throw new ArgumentException($"The buffer does not declare {requiredUsage} usage.", parameterName);
        }
    }

    private static void ValidateBufferTransferRange(long bufferLength, long offset, nint byteLength, string offsetName, string lengthName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset, offsetName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength, lengthName);
        var checkedLength = checked((long)byteLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, bufferLength, offsetName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(checkedLength, bufferLength - offset, lengthName);
    }

    private static void ValidateTextureForTransfer(
        GraphicsDevice device,
        GpuTexture texture,
        TextureUsage requiredUsage,
        string parameterName,
        bool allowRetiring = false)
    {
        ArgumentNullException.ThrowIfNull(texture, parameterName);

        if (!texture.Handle.IsValid
            || (texture.Handle.OwnerId != device.OwnerId)
            || (texture.DeviceEpoch != device.DeviceEpoch)
            || !ReferenceEquals(texture.ResourceOwner.Device, device))
        {
            throw new ArgumentException("The texture must belong to this GraphicsDevice generation.", parameterName);
        }

        if (!allowRetiring && !texture.ResourceOwner.IsLiveForNewWork)
        {
            throw new ObjectDisposedException(parameterName, "The texture no longer accepts new transfer work.");
        }

        if ((texture.Description.Usage & requiredUsage) != requiredUsage)
        {
            throw new ArgumentException($"The texture does not declare {requiredUsage} usage.", parameterName);
        }
    }

    private static async Task WaitForShutdownAndOwnerExitAsync(Task shutdownTask, Thread ownerThread)
    {
        try
        {
            await shutdownTask.ConfigureAwait(false);
        }
        finally
        {
            await Task.Run(ownerThread.Join).ConfigureAwait(false);
        }
    }

    /// <summary>Creates one owned GPU buffer on the dedicated graphics owner.</summary>
    /// <param name="description">The validated buffer size, usage, memory kind, and optional stride.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>The owned buffer after native allocation succeeds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The buffer exceeds the selected device's maximum size.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting resource requests.</exception>
    public async Task<GpuBuffer> CreateBufferAsync(BufferDescription description, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(description);
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting resource requests.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(description.ByteLength, Capabilities.Limits.MaximumBufferByteLength);

        var reservation = await _ownerQueue.ReserveRetirementAsync(cancellationToken).ConfigureAwait(false);
        var request = new CreateBufferRequest(this, description, reservation, cancellationToken);

        try
        {
            await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.AdmissionFailed();
            throw;
        }

        return await request.Task.ConfigureAwait(false);
    }

    /// <summary>Creates one owned GPU pipeline on the dedicated graphics owner.</summary>
    /// <param name="description">The validated graphics or compute pipeline description.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>The owned pipeline after native creation succeeds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting pipeline requests.</exception>
    /// <exception cref="NotSupportedException">The device does not support a shader format, capability, target, or work-group size.</exception>
    /// <exception cref="ObjectDisposedException">A shader bytecode lease is disposed before its independent read credit is acquired.</exception>
    /// <exception cref="GraphicsDeviceLostException">The selected device is lost during pipeline creation.</exception>
    public async Task<GpuPipeline> CreatePipelineAsync(PipelineDescription description, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(description);
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting pipeline requests.");
        }

        ValidatePipelineDescriptionForDevice(description);

        var bytecodeLeases = RetainShaderBytecode(description);
        var bytecodeLeasesTransferred = false;
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation? retirementReservation = null;

        try
        {
            retirementReservation = await _ownerQueue.ReserveRetirementAsync(cancellationToken).ConfigureAwait(false);
            var request = new CreatePipelineRequest(
                this,
                description,
                bytecodeLeases,
                retirementReservation,
                cancellationToken);
            bytecodeLeasesTransferred = true;
            retirementReservation = null;

            try
            {
                await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                request.AdmissionFailed();
                throw;
            }

            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            if (!bytecodeLeasesTransferred)
            {
                ReleaseShaderBytecode(bytecodeLeases);
            }

            retirementReservation?.Dispose();
        }
    }

    /// <summary>Creates one owned GPU sampler on the dedicated graphics owner.</summary>
    /// <param name="description">The validated filtering, addressing, and anisotropy request.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>The owned sampler after native descriptor creation succeeds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting sampler requests.</exception>
    public async Task<GpuSampler> CreateSamplerAsync(SamplerDescription description, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(description);
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting resource requests.");
        }

        var reservation = await _ownerQueue.ReserveRetirementAsync(cancellationToken).ConfigureAwait(false);
        CreateSamplerRequest? request = null;

        try
        {
            request = new CreateSamplerRequest(this, description, reservation, cancellationToken);
            await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (request is null)
            {
                reservation.Dispose();
            }
            else
            {
                request.AdmissionFailed();
            }

            throw;
        }

        return await request.Task.ConfigureAwait(false);
    }

    /// <summary>Creates one owned GPU texture on the dedicated graphics owner.</summary>
    /// <param name="description">The validated extent, format, mip count, and usage.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>The owned texture after native allocation succeeds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A texture extent or array-layer count exceeds the selected device limits.</exception>
    /// <exception cref="NotSupportedException">The selected device does not support the requested format, usage, or sample count.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting resource requests.</exception>
    public async Task<GpuTexture> CreateTextureAsync(TextureDescription description, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(description);
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting resource requests.");
        }

        ValidateTextureCapabilities(description);

        var reservation = await _ownerQueue.ReserveRetirementAsync(cancellationToken).ConfigureAwait(false);
        var request = new CreateTextureRequest(this, description, reservation, cancellationToken);

        try
        {
            await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            request.AdmissionFailed();
            throw;
        }

        return await request.Task.ConfigureAwait(false);
    }

    /// <summary>Reads a valid byte range from a same-device copy-source buffer.</summary>
    /// <param name="source">The GPU source buffer.</param>
    /// <param name="sourceOffset">The nonnegative source byte offset.</param>
    /// <param name="byteLength">The positive byte extent to copy.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>An owned CPU buffer after the native copy fence and CPU ownership transfer complete.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The buffer belongs to another device or lacks copy-source usage.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The requested byte range is invalid.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting readback requests.</exception>
    /// <exception cref="NotSupportedException">The backend does not support the source buffer memory kind.</exception>
    public async Task<OwnedBuffer<byte>> ReadbackBufferAsync(
        GpuBuffer source,
        long sourceOffset,
        nint byteLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBufferForTransfer(this, source, BufferUsage.CopySource, nameof(source));
        ValidateBufferTransferRange(source.ByteLength, sourceOffset, byteLength, nameof(sourceOffset), nameof(byteLength));

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting buffer readbacks.");
        }

        var sourceUse = source.ResourceOwner.AcquireUse(nameof(source));

        try
        {
            var request = new ReadbackBufferRequest(this, source, sourceUse, sourceOffset, byteLength, cancellationToken);
            sourceUse = null;

            try
            {
                await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                request.AdmissionFailed();
                throw;
            }

            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            sourceUse?.Dispose();
        }
    }

    /// <summary>Reads one complete same-device texture subresource into owned CPU storage.</summary>
    /// <param name="source">The GPU source texture.</param>
    /// <param name="subresource">The complete mip, array layer and aspect to copy.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>The owned copy after its graphics fence and CPU transfer complete.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The texture belongs to another device, lacks copy-source usage, or the selected aspect is incompatible.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The selected mip or array layer is outside the texture.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting readback requests.</exception>
    public async Task<TextureReadback> ReadbackTextureAsync(
        GpuTexture source,
        TextureSubresource subresource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTextureForTransfer(this, source, TextureUsage.CopySource, nameof(source));
        _ = GetTextureSubresourceExtent(source.Description, subresource);

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice is not accepting texture readbacks.");
        }

        var sourceUse = source.ResourceOwner.AcquireUse(nameof(source));

        try
        {
            var request = new ReadbackTextureRequest(this, source, sourceUse, subresource, cancellationToken);
            sourceUse = null;

            try
            {
                await _ownerQueue.WriteRequestAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                request.AdmissionFailed();
                throw;
            }

            return await request.Task.ConfigureAwait(false);
        }
        finally
        {
            sourceUse?.Dispose();
        }
    }

    /// <summary>Uploads bytes from an independent read credit into a same-device copy-destination buffer.</summary>
    /// <param name="destination">The GPU destination buffer.</param>
    /// <param name="destinationOffset">The nonnegative destination byte offset.</param>
    /// <param name="source">The caller-owned source lease.</param>
    /// <param name="sourceOffset">The nonnegative offset relative to the source lease.</param>
    /// <param name="byteLength">The positive byte extent to copy.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>An upload receipt after the native copy fence completes.</returns>
    /// <exception cref="ArgumentNullException">A buffer or source lease is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The destination belongs to another device, lacks copy-destination usage, or the source range is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The source or destination byte range is invalid.</exception>
    /// <exception cref="ObjectDisposedException">The source lease or destination buffer has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting upload requests.</exception>
    /// <exception cref="NotSupportedException">The backend does not support the destination memory kind.</exception>
    public async Task<UploadReceipt> UploadBufferAsync(
        GpuBuffer destination,
        long destinationOffset,
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBufferForTransfer(this, destination, BufferUsage.CopyDestination, nameof(destination));
        ValidateBufferTransferRange(destination.ByteLength, destinationOffset, byteLength, nameof(destinationOffset), nameof(byteLength));
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceOffset, source.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(byteLength, source.Length - sourceOffset);

        if (destination.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
        {
            throw new NotSupportedException("Buffer uploads require a device-local destination.");
        }

        var sourceLease = source.Retain();
        return await UploadBufferAsyncCore(
            this,
            destination,
            destinationOffset,
            sourceBytes: null,
            sourceLease,
            sourceOffset,
            byteLength,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Uploads a synchronous copy of a managed byte span into a same-device copy-destination buffer.</summary>
    /// <param name="destination">The GPU destination buffer.</param>
    /// <param name="destinationOffset">The nonnegative destination byte offset.</param>
    /// <param name="source">The caller's bytes, copied synchronously before this method returns.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>An upload receipt after the native copy fence completes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The destination belongs to another device or lacks copy-destination usage.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The destination byte range is invalid.</exception>
    /// <exception cref="InvalidOperationException">The device is not accepting upload requests.</exception>
    /// <exception cref="NotSupportedException">The backend does not support the destination memory kind.</exception>
    public Task<UploadReceipt> UploadBufferAsync(
        GpuBuffer destination,
        long destinationOffset,
        ReadOnlySpan<byte> source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<UploadReceipt>(cancellationToken);
        }

        ValidateBufferForTransfer(this, destination, BufferUsage.CopyDestination, nameof(destination));
        ValidateBufferTransferRange(destination.ByteLength, destinationOffset, source.Length, nameof(destinationOffset), nameof(source));

        if (destination.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
        {
            throw new NotSupportedException("Buffer uploads require a device-local destination.");
        }

        var sourceBytes = source.ToArray();
        return UploadBufferAsyncCore(
            this,
            destination,
            destinationOffset,
            sourceBytes,
            sourceLease: null,
            sourceOffset: 0,
            byteLength: sourceBytes.Length,
            cancellationToken);
    }

    /// <summary>Uploads one complete texture subresource from an independent source read credit.</summary>
    /// <param name="destination">The GPU destination texture.</param>
    /// <param name="subresource">The complete mip, array layer and aspect to copy.</param>
    /// <param name="source">The caller-owned source lease.</param>
    /// <param name="sourceOffset">The nonnegative source offset relative to the lease.</param>
    /// <param name="sourceLayout">The positive row and slice pitches in the source lease.</param>
    /// <param name="cancellationToken">A token that cancels admission or observation before completion.</param>
    /// <returns>An upload receipt after the native copy fence completes.</returns>
    /// <exception cref="ArgumentNullException">The destination texture or source lease is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The destination belongs to another device, lacks copy-destination usage, or the selected aspect is incompatible.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The source offset, pitches, mip, layer, or source extent is invalid.</exception>
    /// <exception cref="ObjectDisposedException">The source lease or destination texture has been disposed.</exception>
    /// <exception cref="NotSupportedException">The selected device does not support this texture transfer.</exception>
    public async Task<UploadReceipt> UploadTextureAsync(
        GpuTexture destination,
        TextureSubresource subresource,
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateTextureForTransfer(this, destination, TextureUsage.CopyDestination, nameof(destination));
        var (Width, Height, Depth) = GetTextureSubresourceExtent(destination.Description, subresource);
        _ = ValidateTextureUploadSource(
            source,
            sourceOffset,
            sourceLayout,
            Width,
            Height,
            Depth,
            destination.Description.Format);

        var sourceLease = source.Retain();
        return await UploadTextureAsyncCore(
            this,
            destination,
            subresource,
            sourceLease,
            sourceOffset,
            sourceLayout,
            cancellationToken).ConfigureAwait(false);
    }

    internal void EnqueueResourceRetirement(GraphicsResourceOwner resource)
    {
        _ = _ownerQueue.TryEnqueueRetirement(
            new RetireResourceRequest(this, resource),
            resource.RetirementReservation);
    }

    internal void NotifyResourceUseReleased()
        => _ownerQueue.SignalOwnerWork();

    internal Task ShutdownAsync()
    {
        while (true)
        {
            var currentStatus = Volatile.Read(ref _status);

            if (currentStatus is (int)GraphicsDeviceStatus.Stopping or (int)GraphicsDeviceStatus.Disposed)
            {
                return _shutdownTask;
            }

            if ((currentStatus == (int)GraphicsDeviceStatus.None)
                || !Enum.IsDefined((GraphicsDeviceStatus)currentStatus))
            {
                throw new InvalidOperationException("The GraphicsDevice has an invalid lifecycle state.");
            }

            if (Interlocked.CompareExchange(ref _status, (int)GraphicsDeviceStatus.Stopping, currentStatus) == currentStatus)
            {
                _ownerQueue.CompleteRequests();
                return _shutdownTask;
            }
        }
    }

    private GpuBuffer CreateBufferOnOwner(
        BufferDescription description,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the buffer request was executed.");
        }

        var allocationInfo = backendDevice.GetBufferAllocationInfo(description);
        ArgumentOutOfRangeException.ThrowIfLessThan(allocationInfo.SizeBytes, description.ByteLength);
        ReserveResourceAllocation(description.MemoryKind, allocationInfo);

        IGraphicsBackendBuffer? backendBuffer = null;

        try
        {
            backendBuffer = backendDevice.CreateBuffer(description);
            var handle = new GpuBufferHandle(_ownerId, DeviceEpoch, GetNextBufferIndex(), generation: 1);
            var resourceOwner = new GraphicsResourceOwner(
                this,
                allocationInfo,
                backendBuffer,
                retirementReservation,
                bufferDescription: description);
            var buffer = new GpuBuffer(handle, description, DeviceEpoch, resourceOwner);

            if (!_resources.Add(resourceOwner))
            {
                throw new InvalidOperationException("The newly created Graphics resource already exists in its owner registry.");
            }

            backendBuffer = null;
            return buffer;
        }
        catch (Exception exception)
        {
            if (backendBuffer is not null)
            {
                try
                {
                    backendBuffer.Dispose();
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendResource(backendBuffer, new AggregateException(exception, cleanupException));
                    throw new AggregateException(exception, cleanupException);
                }
            }

            ReleaseResourceAllocation(allocationInfo);
            throw;
        }
    }

    private GpuPipeline CreatePipelineOnOwner(
        PipelineDescription description,
        ReadOnlySpan<BufferReadLease<byte>> bytecodeLeases,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the pipeline request was executed.");
        }

        IGraphicsBackendPipeline? backendPipeline = null;

        try
        {
            backendPipeline = backendDevice.CreatePipeline(description, bytecodeLeases);
            var handle = new GpuPipelineHandle(_ownerId, DeviceEpoch, GetNextPipelineIndex(), generation: 1);
            var resourceOwner = new GraphicsResourceOwner(this, allocationInfo: null, backendPipeline, retirementReservation);
            var pipeline = new GpuPipeline(handle, DeviceEpoch, resourceOwner);

            if (!_resources.Add(resourceOwner))
            {
                throw new InvalidOperationException("The newly created Graphics pipeline already exists in its owner registry.");
            }

            backendPipeline = null;
            return pipeline;
        }
        catch (Exception exception)
        {
            if (backendPipeline is not null)
            {
                try
                {
                    backendPipeline.Dispose();
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendResource(backendPipeline, new AggregateException(exception, cleanupException));
                    throw new AggregateException(exception, cleanupException);
                }
            }

            throw;
        }
    }

    private GpuSampler CreateSamplerOnOwner(
        SamplerDescription description,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the sampler request was executed.");
        }

        var effectiveMaximumAnisotropy = Math.Min(description.MaximumAnisotropy, Capabilities.Limits.MaximumSamplerAnisotropy);
        IGraphicsBackendSampler? backendSampler = null;

        try
        {
            backendSampler = backendDevice.CreateSampler(description, effectiveMaximumAnisotropy);
            var handle = new GpuSamplerHandle(_ownerId, DeviceEpoch, GetNextSamplerIndex(), generation: 1);
            var resourceOwner = new GraphicsResourceOwner(this, allocationInfo: null, backendSampler, retirementReservation);
            var sampler = new GpuSampler(handle, DeviceEpoch, resourceOwner);

            if (!_resources.Add(resourceOwner))
            {
                throw new InvalidOperationException("The newly created Graphics sampler already exists in its owner registry.");
            }

            backendSampler = null;
            return sampler;
        }
        catch (Exception exception)
        {
            if (backendSampler is not null)
            {
                try
                {
                    backendSampler.Dispose();
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendResource(backendSampler, new AggregateException(exception, cleanupException));
                    throw new AggregateException(exception, cleanupException);
                }
            }

            throw;
        }
    }

    private GpuTexture CreateTextureOnOwner(
        TextureDescription description,
        GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the texture request was executed.");
        }

        var allocationInfo = backendDevice.GetTextureAllocationInfo(description);
        ReserveResourceAllocation(GpuMemoryKind.DeviceLocal, allocationInfo);

        IGraphicsBackendTexture? backendTexture = null;

        try
        {
            backendTexture = backendDevice.CreateTexture(description);
            var handle = new GpuTextureHandle(_ownerId, DeviceEpoch, GetNextTextureIndex(), generation: 1);
            var resourceOwner = new GraphicsResourceOwner(
                this,
                allocationInfo,
                backendTexture,
                retirementReservation,
                textureDescription: description);
            var texture = new GpuTexture(handle, description, DeviceEpoch, resourceOwner);

            if (!_resources.Add(resourceOwner))
            {
                throw new InvalidOperationException("The newly created Graphics texture already exists in its owner registry.");
            }

            backendTexture = null;
            return texture;
        }
        catch (Exception exception)
        {
            if (backendTexture is not null)
            {
                try
                {
                    backendTexture.Dispose();
                }
                catch (Exception cleanupException)
                {
                    QuarantineBackendResource(backendTexture, new AggregateException(exception, cleanupException));
                    throw new AggregateException(exception, cleanupException);
                }
            }

            ReleaseResourceAllocation(allocationInfo);
            throw;
        }
    }

    private uint GetNextBufferIndex()
    {
        if (_nextBufferIndex > uint.MaxValue)
        {
            throw new InvalidOperationException("The GraphicsDevice exhausted its buffer handle slots.");
        }

        return (uint)_nextBufferIndex++;
    }

    private uint GetNextPipelineIndex()
    {
        if (_nextPipelineIndex > uint.MaxValue)
        {
            throw new InvalidOperationException("The GraphicsDevice exhausted its pipeline handle slots.");
        }

        return (uint)_nextPipelineIndex++;
    }

    private uint GetNextSamplerIndex()
    {
        if (_nextSamplerIndex > uint.MaxValue)
        {
            throw new InvalidOperationException("The GraphicsDevice exhausted its sampler handle slots.");
        }

        return (uint)_nextSamplerIndex++;
    }

    private uint GetNextTextureIndex()
    {
        if (_nextTextureIndex > uint.MaxValue)
        {
            throw new InvalidOperationException("The GraphicsDevice exhausted its texture handle slots.");
        }

        return (uint)_nextTextureIndex++;
    }

    private void MarkDeviceLost()
    {
        while (true)
        {
            var currentStatus = Volatile.Read(ref _status);

            if (currentStatus != (int)GraphicsDeviceStatus.Ready)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _status, (int)GraphicsDeviceStatus.Lost, currentStatus) == currentStatus)
            {
                _ownerQueue.CompleteRequests();
                _ownerQueue.CompleteRetirementReservations();
                return;
            }
        }
    }

    private void QuarantineBackendResource(IGraphicsBackendResource backendResource, Exception exception)
    {
        s_quarantinedBackendResources.Add(backendResource);
        s_quarantinedGraphicsDevices.Add(this);
        _hasQuarantinedResources = true;
        _retirementFailure ??= exception;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Shutdown failure handling must continue quarantining other accepted work and resources after an individual cleanup failure.")]
    private Exception QuarantineOwnerState(Exception exception)
    {
        _ownerQueue.CompleteRequests();
        _ownerQueue.CompleteRetirements();

        while (_ownerQueue.TryReadRequest(out var request))
        {
            try
            {
                request.Fail(exception);
            }
            catch (Exception failureException)
            {
                exception = new AggregateException(exception, failureException);
            }
        }

        while (_ownerQueue.TryReadRetirement(out var retirement))
        {
            try
            {
                retirement.Fail(exception);
            }
            catch (Exception failureException)
            {
                exception = new AggregateException(exception, failureException);
            }
        }

        var activeResources = new GraphicsResourceOwner[_resources.Count];
        _resources.CopyTo(activeResources);

        foreach (var resource in activeResources)
        {
            try
            {
                QuarantineResource(resource, exception);
            }
            catch (Exception failureException)
            {
                exception = new AggregateException(exception, failureException);
            }
        }

        s_quarantinedGraphicsDevices.Add(this);
        return exception;
    }

    private void QuarantineResource(GraphicsResourceOwner resource, Exception exception)
    {
        resource.MarkQuarantined();
        _ = _deferredRetirements.Remove(resource);
        s_quarantinedResources.Add(resource);
        s_quarantinedGraphicsDevices.Add(this);
        _hasQuarantinedResources = true;
        _retirementFailure ??= exception;
        resource.RetirementReservation.Dispose();
    }

    private OwnedBuffer<byte> ReadbackBufferOnOwner(GpuBuffer source, long sourceOffset, nint byteLength)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the readback request was executed.");
        }

        ValidateBufferForTransfer(this, source, BufferUsage.CopySource, nameof(source), allowRetiring: true);

        if (source.ResourceOwner.BackendResource is not IGraphicsBackendBuffer backendBuffer)
        {
            throw new InvalidOperationException("The source buffer has no backend buffer owner.");
        }

        var result = backendDevice.ReadbackBuffer(backendBuffer, sourceOffset, byteLength);
        source.ResourceOwner.RecordBufferReadback();
        return result;
    }

    private TextureReadback ReadbackTextureOnOwner(GpuTexture source, TextureSubresource subresource)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the texture readback request was executed.");
        }

        ValidateTextureForTransfer(this, source, TextureUsage.CopySource, nameof(source), allowRetiring: true);
        _ = GetTextureSubresourceExtent(source.Description, subresource);

        if (source.ResourceOwner.BackendResource is not IGraphicsBackendTexture backendTexture)
        {
            throw new InvalidOperationException("The source texture has no backend texture owner.");
        }

        var result = backendDevice.ReadbackTexture(backendTexture, subresource);
        source.ResourceOwner.RecordTextureReadback(subresource);
        return result;
    }

    private void ReleaseResourceAllocation(GraphicsResourceAllocationInfo allocationInfo)
    {
        if (!_allocatedBytesByHeap.TryGetValue(allocationInfo.HeapIndex, out var heapBytes)
            || (heapBytes < allocationInfo.SizeBytes))
        {
            throw new InvalidOperationException("The Graphics resource allocation budget was released inconsistently.");
        }

        var remainingTotalBytes = checked(_allocatedResourceBytes - allocationInfo.SizeBytes);
        var remainingBytes = heapBytes - allocationInfo.SizeBytes;

        if (remainingBytes == 0)
        {
            _ = _allocatedBytesByHeap.Remove(allocationInfo.HeapIndex);
        }
        else
        {
            _allocatedBytesByHeap[allocationInfo.HeapIndex] = remainingBytes;
        }

        _allocatedResourceBytes = remainingTotalBytes;
    }

    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "The Graphics allocation contract uses contextual OutOfMemoryException for exhausted resource budgets.")]
    private void ReserveResourceAllocation(GpuMemoryKind memoryKind, GraphicsResourceAllocationInfo allocationInfo)
    {
        if (allocationInfo.SizeBytes > (DefaultOwnedAllocationBudgetBytes - _allocatedResourceBytes))
        {
            throw new OutOfMemoryException("The GraphicsDevice's persistent allocation budget is exhausted.");
        }

        GraphicsMemoryHeapCapabilities? selectedHeap = null;

        foreach (var heap in Capabilities.MemoryHeaps)
        {
            if (heap.Index == allocationInfo.HeapIndex)
            {
                selectedHeap = heap;
                break;
            }
        }

        if (selectedHeap is null)
        {
            throw new NotSupportedException($"The backend selected unreported memory heap {allocationInfo.HeapIndex}.");
        }

        var requiredHeapProperties = memoryKind == GpuMemoryKind.DeviceLocal
            ? GraphicsMemoryHeapProperties.DeviceLocal
            : GraphicsMemoryHeapProperties.HostVisible;

        if ((selectedHeap.Properties & requiredHeapProperties) != requiredHeapProperties)
        {
            throw new NotSupportedException(
                $"The backend selected memory heap {allocationInfo.HeapIndex} without the required {requiredHeapProperties} property.");
        }

        var heapBudget = allocationInfo.BudgetBytes ?? selectedHeap.BudgetBytes ?? selectedHeap.SizeBytes;
        var allocatedOnHeap = _allocatedBytesByHeap.GetValueOrDefault(allocationInfo.HeapIndex);

        if (allocationInfo.SizeBytes > (heapBudget - allocatedOnHeap))
        {
            throw new OutOfMemoryException($"The GraphicsDevice's native memory heap {allocationInfo.HeapIndex} budget is exhausted.");
        }

        var totalBytes = checked(_allocatedResourceBytes + allocationInfo.SizeBytes);
        var heapBytes = checked(allocatedOnHeap + allocationInfo.SizeBytes);
        _allocatedBytesByHeap[allocationInfo.HeapIndex] = heapBytes;
        _allocatedResourceBytes = totalBytes;
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A deferred native-release failure is quarantined without terminating unrelated owner work.")]
    private void RetireDeferredResourcesOnOwner()
    {
        if (_deferredRetirements.Count == 0)
        {
            return;
        }

        var deferredResources = new GraphicsResourceOwner[_deferredRetirements.Count];
        _deferredRetirements.CopyTo(deferredResources);

        foreach (var resource in deferredResources)
        {
            if (!resource.HasActiveUses)
            {
                try
                {
                    RetireResourceOnOwner(resource);
                }
                catch (Exception exception)
                {
                    QuarantineResource(resource, exception);
                }
            }
        }
    }

    private void RetireResourceOnOwner(GraphicsResourceOwner resource)
    {
        if (!resource.TryBeginRetirement())
        {
            if (resource.HasActiveUses && resource.IsRetirementRequested)
            {
                _ = _deferredRetirements.Add(resource);
            }
            else
            {
                _ = _deferredRetirements.Remove(resource);
            }

            return;
        }

        _ = _deferredRetirements.Remove(resource);
        resource.BackendResource.Dispose();

        if (!_resources.Remove(resource))
        {
            throw new InvalidOperationException("A retired Graphics resource was not registered with its owner.");
        }

        if (resource.AllocationInfo is GraphicsResourceAllocationInfo allocationInfo)
        {
            ReleaseResourceAllocation(allocationInfo);
        }

        resource.RetirementReservation.Dispose();
        resource.MarkRetired();
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Every failed native release is quarantined and retained while shutdown proceeds for unrelated resources.")]
    private void RetireResourcesForShutdown()
    {
        var activeResources = new GraphicsResourceOwner[_resources.Count];
        _resources.CopyTo(activeResources);

        foreach (var resource in activeResources)
        {
            resource.RequestRetirementForShutdown();

            try
            {
                RetireResourceOnOwner(resource);
            }
            catch (Exception exception)
            {
                QuarantineResource(resource, exception);
            }
        }

        while (_deferredRetirements.Count > 0)
        {
            RetireDeferredResourcesOnOwner();

            if (_deferredRetirements.Count > 0)
            {
                _ownerQueue.WaitForWork();
            }
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The owner thread must relay initialization and native-release failures through its startup or shutdown task.")]
    private void RunOwnerLoop()
    {
        try
        {
            while (true)
            {
                var didWork = false;

                if (_ownerQueue.TryReadRetirement(out var retirement))
                {
                    ExecuteOwnerWork(retirement, this);
                    didWork = true;
                }

                if (_ownerQueue.TryReadRequest(out var request))
                {
                    ExecuteOwnerWork(request, this);
                    didWork = true;
                }

                RetireDeferredResourcesOnOwner();

                if (_ownerQueue.RequestsCompleted && Status == GraphicsDeviceStatus.Stopping)
                {
                    _ownerQueue.CompleteRetirements();

                    while (_ownerQueue.TryReadRetirement(out retirement))
                    {
                        ExecuteOwnerWork(retirement, this);
                    }

                    RetireResourcesForShutdown();

                    if (_hasQuarantinedResources)
                    {
                        s_quarantinedGraphicsDevices.Add(this);
                        Volatile.Write(ref _status, (int)GraphicsDeviceStatus.Stopping);
                        _ = _shutdownCompletion.TrySetException(
                            _retirementFailure
                            ?? new InvalidOperationException("A Graphics resource could not be retired safely."));
                        return;
                    }

                    break;
                }

                if (!didWork)
                {
                    _ownerQueue.WaitForWork();
                }
            }

            var backendDevice = _backendDevice
                ?? throw new InvalidOperationException("A ready GraphicsDevice must own its backend device.");
            backendDevice.Dispose();
            _backendDevice = null;
            Volatile.Write(ref _status, (int)GraphicsDeviceStatus.Disposed);
            _ = _shutdownCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            exception = QuarantineOwnerState(exception);
            var backendDevice = _backendDevice;

            if (backendDevice is not null)
            {
                s_quarantinedBackendDevices.Add(backendDevice);
                _backendDevice = null;
            }

            Volatile.Write(ref _status, (int)GraphicsDeviceStatus.Stopping);
            _ = _shutdownCompletion.TrySetException(exception);
        }
        finally
        {
            _ownerQueue.Dispose();
        }
    }

    private long UploadBufferOnOwner(
        GpuBuffer destination,
        long destinationOffset,
        BufferReadLease<byte> source,
        nint sourceOffset,
        nint byteLength)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the upload request was executed.");
        }

        ValidateBufferForTransfer(this, destination, BufferUsage.CopyDestination, nameof(destination), allowRetiring: true);

        if (destination.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
        {
            throw new NotSupportedException("Buffer uploads require a device-local destination.");
        }

        if (destination.ResourceOwner.BackendResource is not IGraphicsBackendBuffer backendBuffer)
        {
            throw new InvalidOperationException("The destination buffer has no backend buffer owner.");
        }

        var completedFence = backendDevice.UploadBuffer(backendBuffer, destinationOffset, source, sourceOffset, byteLength);
        destination.ResourceOwner.RecordBufferUpload(destinationOffset, byteLength);
        return completedFence;
    }

    private long UploadBufferOnOwner(GpuBuffer destination, long destinationOffset, ReadOnlySpan<byte> source)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the upload request was executed.");
        }

        ValidateBufferForTransfer(this, destination, BufferUsage.CopyDestination, nameof(destination), allowRetiring: true);

        if (destination.Description.MemoryKind != GpuMemoryKind.DeviceLocal)
        {
            throw new NotSupportedException("Buffer uploads require a device-local destination.");
        }

        if (destination.ResourceOwner.BackendResource is not IGraphicsBackendBuffer backendBuffer)
        {
            throw new InvalidOperationException("The destination buffer has no backend buffer owner.");
        }

        var completedFence = backendDevice.UploadBuffer(backendBuffer, destinationOffset, source);
        destination.ResourceOwner.RecordBufferUpload(destinationOffset, source.Length);
        return completedFence;
    }

    private long UploadTextureOnOwner(
        GpuTexture destination,
        TextureSubresource subresource,
        BufferReadLease<byte> source,
        nint sourceOffset,
        TextureDataLayout sourceLayout)
    {
        var backendDevice = _backendDevice
            ?? throw new InvalidOperationException("The GraphicsDevice has no live backend owner.");

        if (Status != GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("The GraphicsDevice stopped before the texture upload request was executed.");
        }

        ValidateTextureForTransfer(this, destination, TextureUsage.CopyDestination, nameof(destination), allowRetiring: true);
        var (Width, Height, Depth) = GetTextureSubresourceExtent(destination.Description, subresource);
        _ = ValidateTextureUploadSource(
            source,
            sourceOffset,
            sourceLayout,
            Width,
            Height,
            Depth,
            destination.Description.Format);

        if (destination.ResourceOwner.BackendResource is not IGraphicsBackendTexture backendTexture)
        {
            throw new InvalidOperationException("The destination texture has no backend texture owner.");
        }

        var completedFence = backendDevice.UploadTexture(backendTexture, subresource, source, sourceOffset, sourceLayout);
        destination.ResourceOwner.RecordTextureUpload(subresource);
        return completedFence;
    }

    private void ValidatePipelineDescriptionForDevice(PipelineDescription description)
    {
        var requiredShaderFormat = Capabilities.Backend switch {
            GraphicsBackend.D3D12 => ShaderBinaryFormat.Dxil,
            GraphicsBackend.Vulkan => ShaderBinaryFormat.SpirV,
            _ => throw new NotSupportedException($"The Graphics backend {Capabilities.Backend} cannot create pipelines."),
        };
        var shaderInputs = GetPipelineShaderInputs(description);

        for (var index = 0; index < shaderInputs.Length; index++)
        {
            var shader = shaderInputs[index];

            if (shader.Format != requiredShaderFormat)
            {
                throw new NotSupportedException(
                    $"The {Capabilities.Backend} backend requires {requiredShaderFormat} bytecode for {shader.Stage} shaders.");
            }

            if (!shader.Reflection.RequiredCapabilities.IsEmpty)
            {
                throw new NotSupportedException(
                    $"The {Capabilities.Backend} backend has no qualified mapping for shader capabilities {shader.Reflection.RequiredCapabilities}.");
            }

            ValidatePipelineResourceLimits(shader.Reflection);
        }

        if (description is ComputePipelineDescription computeDescription)
        {
            var limits = Capabilities.Limits;
            var reflection = computeDescription.Shader.Reflection;

            if ((reflection.GroupSizeX > limits.MaximumComputeWorkGroupSizeX)
                || (reflection.GroupSizeY > limits.MaximumComputeWorkGroupSizeY)
                || (reflection.GroupSizeZ > limits.MaximumComputeWorkGroupSizeZ)
                || ((long)reflection.GroupSizeX * reflection.GroupSizeY * reflection.GroupSizeZ > limits.MaximumComputeWorkGroupInvocations))
            {
                throw new NotSupportedException("The compute shader work-group size exceeds the selected device limits.");
            }

            return;
        }

        var graphicsDescription = (GraphicsPipelineDescription)description;

        if (graphicsDescription.VertexElements.Length > Capabilities.Limits.MaximumVertexInputAttributes)
        {
            throw new NotSupportedException("The graphics pipeline vertex layout exceeds the selected device attribute limit.");
        }

        if (graphicsDescription.ColorTargetFormats.Length > Capabilities.Limits.MaximumColorAttachments)
        {
            throw new NotSupportedException("The graphics pipeline exceeds the selected device color-attachment limit.");
        }

        for (var index = 0; index < graphicsDescription.ColorTargetFormats.Length; index++)
        {
            ValidatePipelineTargetFormat(
                graphicsDescription.ColorTargetFormats[index],
                TextureUsage.ColorAttachment);
        }

        if (graphicsDescription.DepthTargetFormat is TextureFormat depthTargetFormat)
        {
            ValidatePipelineTargetFormat(depthTargetFormat, TextureUsage.DepthAttachment);
        }
    }

    private void ValidatePipelineResourceLimits(ShaderReflection reflection)
    {
        var limits = Capabilities.Limits;
        var sampledImageCount = 0;
        var samplerCount = 0;
        var uniformBufferCount = 0;

        for (var index = 0; index < reflection.Resources.Length; index++)
        {
            var resource = reflection.Resources[index];

            switch (resource.Kind)
            {
                case ShaderResourceKind.SampledImage:
                {
                    sampledImageCount = checked(sampledImageCount + resource.ArrayCount);
                    break;
                }

                case ShaderResourceKind.Sampler:
                {
                    samplerCount = checked(samplerCount + resource.ArrayCount);
                    break;
                }

                case ShaderResourceKind.UniformBuffer:
                {
                    uniformBufferCount = checked(uniformBufferCount + resource.ArrayCount);
                    break;
                }

                case ShaderResourceKind.StorageImage:
                case ShaderResourceKind.StorageBuffer:
                {
                    break;
                }

                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(reflection), "Shader reflection contains an undefined resource kind.");
                }
            }
        }

        if (sampledImageCount > limits.MaximumSampledImagesPerStage)
        {
            throw new NotSupportedException(
                $"The {reflection.Stage} shader uses {sampledImageCount} sampled images, exceeding the device limit of {limits.MaximumSampledImagesPerStage}.");
        }

        if (samplerCount > limits.MaximumSamplersPerStage)
        {
            throw new NotSupportedException(
                $"The {reflection.Stage} shader uses {samplerCount} samplers, exceeding the device limit of {limits.MaximumSamplersPerStage}.");
        }

        if (uniformBufferCount > limits.MaximumUniformBuffersPerStage)
        {
            throw new NotSupportedException(
                $"The {reflection.Stage} shader uses {uniformBufferCount} uniform buffers, exceeding the device limit of {limits.MaximumUniformBuffersPerStage}.");
        }
    }

    private void ValidatePipelineTargetFormat(TextureFormat format, TextureUsage requiredUsage)
    {
        for (var index = 0; index < Capabilities.Formats.Length; index++)
        {
            var formatCapabilities = Capabilities.Formats[index];

            if (formatCapabilities.Format != format)
            {
                continue;
            }

            var supportsSingleSample = false;

            for (var sampleIndex = 0; sampleIndex < formatCapabilities.SampleCounts.Length; sampleIndex++)
            {
                if (formatCapabilities.SampleCounts[sampleIndex] == 1)
                {
                    supportsSingleSample = true;
                    break;
                }
            }

            if ((formatCapabilities.SupportedUsages & requiredUsage) != requiredUsage || !supportsSingleSample)
            {
                throw new NotSupportedException($"The selected device does not support {requiredUsage} for {format}.");
            }

            return;
        }

        throw new NotSupportedException($"The selected device does not support texture format {format}.");
    }

    private void ValidateTextureCapabilities(TextureDescription description)
    {
        var limits = Capabilities.Limits;

        if (description.Dimension == TextureDimension.Dimension2D)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(description.Width, limits.MaximumTextureDimension2D);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(description.Height, limits.MaximumTextureDimension2D);
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(description.Width, limits.MaximumTextureDimension3D);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(description.Height, limits.MaximumTextureDimension3D);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(description.Depth, limits.MaximumTextureDimension3D);
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(description.ArrayLayers, limits.MaximumArrayLayers);

        GraphicsFormatCapabilities? formatCapabilities = null;

        foreach (var candidate in Capabilities.Formats)
        {
            if (candidate.Format == description.Format)
            {
                formatCapabilities = candidate;
                break;
            }
        }

        if (formatCapabilities is null)
        {
            throw new NotSupportedException($"The selected device does not support texture format {description.Format}.");
        }

        if ((formatCapabilities.SupportedUsages & description.Usage) != description.Usage)
        {
            throw new NotSupportedException(
                $"The selected device does not support texture usages {description.Usage} for {description.Format}.");
        }

        foreach (var sampleCount in formatCapabilities.SampleCounts)
        {
            if (sampleCount == 1)
            {
                return;
            }
        }

        throw new NotSupportedException($"The selected device does not support single-sample texture format {description.Format}.");
    }

    private sealed class CreateBufferRequest : IGraphicsDeviceWork
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GpuBuffer> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GpuBuffer? _createdBuffer;
        private readonly BufferDescription _description;
        private readonly GraphicsDevice _device;
        private GraphicsOwnerRequestQueue.GraphicsRetirementReservation? _retirementReservation;
        // 0 is pending, 1 is canceled observation, and 2 is terminal.
        private int _state;

        internal CreateBufferRequest(
            GraphicsDevice device,
            BufferDescription description,
            GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation,
            CancellationToken cancellationToken)
        {
            _description = description;
            _device = device;
            _retirementReservation = retirementReservation;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((CreateBufferRequest)state!).Cancel(),
                this);
        }

        internal Task<GpuBuffer> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseReservation();
                _cancellationRegistration.Dispose();
                return;
            }

            try
            {
                var reservation = _retirementReservation
                    ?? throw new InvalidOperationException("The buffer request has no retirement reservation.");
                var buffer = device.CreateBufferOnOwner(_description, reservation);
                _createdBuffer = buffer;
                _retirementReservation = null;

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    _ = _completion.TrySetResult(buffer);
                }
                else
                {
                    device.RetireResourceOnOwner(buffer.ResourceOwner);
                }
            }
            finally
            {
                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            if (_createdBuffer is not null)
            {
                _device.QuarantineResource(_createdBuffer.ResourceOwner, exception);
            }

            ReleaseReservation();

            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseReservation();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseReservation()
        {
            var reservation = Interlocked.Exchange(ref _retirementReservation, null);
            reservation?.Dispose();
        }
    }

    private sealed class CreatePipelineRequest : IGraphicsDeviceWork
    {
        private BufferReadLease<byte>[]? _bytecodeLeases;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GpuPipeline> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GpuPipeline? _createdPipeline;
        private readonly PipelineDescription _description;
        private readonly GraphicsDevice _device;
        private int _nativeCreationStarted;
        private GraphicsOwnerRequestQueue.GraphicsRetirementReservation? _retirementReservation;
        // 0 is pending, 1 is canceled observation, and 2 is terminal.
        private int _state;

        internal CreatePipelineRequest(
            GraphicsDevice device,
            PipelineDescription description,
            BufferReadLease<byte>[] bytecodeLeases,
            GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation,
            CancellationToken cancellationToken)
        {
            _bytecodeLeases = bytecodeLeases;
            _description = description;
            _device = device;
            _retirementReservation = retirementReservation;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((CreatePipelineRequest)state!).Cancel(),
                this);
        }

        internal Task<GpuPipeline> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseBytecodeLeases();
                ReleaseReservation();
                CompleteCanceled();
                _cancellationRegistration.Dispose();
                return;
            }

            GpuPipeline? completedPipeline = null;
            var completeCanceled = false;

            try
            {
                var reservation = _retirementReservation
                    ?? throw new InvalidOperationException("The pipeline request has no retirement reservation.");
                var bytecodeLeases = _bytecodeLeases
                    ?? throw new InvalidOperationException("The pipeline request has no bytecode leases.");
                Volatile.Write(ref _nativeCreationStarted, 1);
                var pipeline = device.CreatePipelineOnOwner(_description, bytecodeLeases, reservation);
                _createdPipeline = pipeline;
                _retirementReservation = null;

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    completedPipeline = pipeline;
                }
                else
                {
                    device.RetireResourceOnOwner(pipeline.ResourceOwner);
                    completeCanceled = true;
                }
            }
            finally
            {
                ReleaseBytecodeLeases();

                if (completedPipeline is not null)
                {
                    _ = _completion.TrySetResult(completedPipeline);
                }
                else if (completeCanceled)
                {
                    CompleteCanceled();
                }

                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            if (_createdPipeline is not null)
            {
                _device.QuarantineResource(_createdPipeline.ResourceOwner, exception);
            }

            var previousState = Interlocked.Exchange(ref _state, 2);
            ReleaseBytecodeLeases();
            ReleaseReservation();

            if ((previousState == 1) && (Volatile.Read(ref _nativeCreationStarted) == 0))
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseBytecodeLeases();
            ReleaseReservation();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
        {
            _ = Interlocked.CompareExchange(ref _state, 1, 0);
        }

        private void CompleteCanceled()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) == 1)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseBytecodeLeases()
        {
            var bytecodeLeases = Interlocked.Exchange(ref _bytecodeLeases, null);

            if (bytecodeLeases is null)
            {
                return;
            }

            ReleaseShaderBytecode(bytecodeLeases);
        }

        private void ReleaseReservation()
        {
            var reservation = Interlocked.Exchange(ref _retirementReservation, null);
            reservation?.Dispose();
        }
    }

    private sealed class CreateSamplerRequest : IGraphicsDeviceWork
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GpuSampler> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GpuSampler? _createdSampler;
        private readonly SamplerDescription _description;
        private readonly GraphicsDevice _device;
        private GraphicsOwnerRequestQueue.GraphicsRetirementReservation? _retirementReservation;
        private int _state;

        internal CreateSamplerRequest(
            GraphicsDevice device,
            SamplerDescription description,
            GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(description);
            ArgumentNullException.ThrowIfNull(retirementReservation);
            _description = description;
            _device = device;
            _retirementReservation = retirementReservation;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((CreateSamplerRequest)state!).Cancel(),
                this);
        }

        internal Task<GpuSampler> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseReservation();
                _cancellationRegistration.Dispose();
                return;
            }

            try
            {
                var reservation = _retirementReservation
                    ?? throw new InvalidOperationException("The sampler request has no retirement reservation.");
                var sampler = device.CreateSamplerOnOwner(_description, reservation);
                _createdSampler = sampler;
                _retirementReservation = null;

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    _ = _completion.TrySetResult(sampler);
                }
                else
                {
                    device.RetireResourceOnOwner(sampler.ResourceOwner);
                }
            }
            finally
            {
                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            if (_createdSampler is not null)
            {
                _device.QuarantineResource(_createdSampler.ResourceOwner, exception);
            }

            ReleaseReservation();

            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseReservation();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseReservation()
        {
            var reservation = Interlocked.Exchange(ref _retirementReservation, null);
            reservation?.Dispose();
        }
    }

    private sealed class CreateTextureRequest : IGraphicsDeviceWork
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GpuTexture> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GpuTexture? _createdTexture;
        private readonly TextureDescription _description;
        private readonly GraphicsDevice _device;
        private GraphicsOwnerRequestQueue.GraphicsRetirementReservation? _retirementReservation;
        // 0 is pending, 1 is canceled observation, and 2 is terminal.
        private int _state;

        internal CreateTextureRequest(
            GraphicsDevice device,
            TextureDescription description,
            GraphicsOwnerRequestQueue.GraphicsRetirementReservation retirementReservation,
            CancellationToken cancellationToken)
        {
            _description = description;
            _device = device;
            _retirementReservation = retirementReservation;
            _cancellationToken = cancellationToken;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((CreateTextureRequest)state!).Cancel(),
                this);
        }

        internal Task<GpuTexture> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseReservation();
                _cancellationRegistration.Dispose();
                return;
            }

            try
            {
                var reservation = _retirementReservation
                    ?? throw new InvalidOperationException("The texture request has no retirement reservation.");
                var texture = device.CreateTextureOnOwner(_description, reservation);
                _createdTexture = texture;
                _retirementReservation = null;

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    _ = _completion.TrySetResult(texture);
                }
                else
                {
                    device.RetireResourceOnOwner(texture.ResourceOwner);
                }
            }
            finally
            {
                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            if (_createdTexture is not null)
            {
                _device.QuarantineResource(_createdTexture.ResourceOwner, exception);
            }

            ReleaseReservation();

            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseReservation();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseReservation()
        {
            var reservation = Interlocked.Exchange(ref _retirementReservation, null);
            reservation?.Dispose();
        }
    }

    private sealed class GraphicsDeviceStartup
    {
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<GraphicsDevice> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly uint _ownerId;
        private readonly GraphicsPlatformKind _platformKind;
        private readonly GraphicsDeviceProfile _profile;
        private readonly GraphicsBackendRegistration _registration;

        internal GraphicsDeviceStartup(
            uint ownerId,
            GraphicsBackendRegistration registration,
            GraphicsDeviceProfile profile,
            GraphicsPlatformKind platformKind,
            CancellationToken cancellationToken)
        {
            _ownerId = ownerId;
            _registration = registration;
            _profile = profile;
            _platformKind = platformKind;
            _cancellationToken = cancellationToken;
        }

        internal Task<GraphicsDevice> Start()
        {
            var ownerThread = new Thread(RunOwner) {
                IsBackground = true,
                Name = "TerraFX.Graphics.Owner",
            };
            ownerThread.Start();
            return _completion.Task;
        }

        [SuppressMessage(
            "Usage",
            "CA1031:Do not catch general exception types",
            Justification = "The startup task must receive factory, admission, cancellation, and unwind failures from the dedicated owner thread.")]
        private void RunOwner()
        {
            IGraphicsBackendDevice? backendDevice = null;

            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                backendDevice = _registration.Factory.Create(
                    _ownerId,
                    _profile,
                    _platformKind,
                    _cancellationToken);
                ArgumentNullException.ThrowIfNull(backendDevice);
                _cancellationToken.ThrowIfCancellationRequested();

                var capabilities = backendDevice.Capabilities;

                if (capabilities.Backend != _registration.Backend)
                {
                    throw new InvalidOperationException("The registered factory created a device for a different backend.");
                }

                var missingRequirements = GraphicsCapabilityAdmission.GetMissingRequirements(capabilities, _profile);

                if (!missingRequirements.IsEmpty)
                {
                    throw new NotSupportedException(
                        $"The selected adapter does not satisfy the {_profile} profile: {string.Join("; ", missingRequirements)}.");
                }

                _cancellationToken.ThrowIfCancellationRequested();

                var device = new GraphicsDevice(_ownerId, backendDevice);
                backendDevice = null;
                _completion.SetResult(device);
                device.RunOwnerLoop();
            }
            catch (Exception exception)
            {
                if (backendDevice is not null)
                {
                    try
                    {
                        backendDevice.Dispose();
                    }
                    catch (Exception cleanupException)
                    {
                        s_quarantinedBackendDevices.Add(backendDevice);
                        exception = new AggregateException(exception, cleanupException);
                    }
                }

                if (_cancellationToken.IsCancellationRequested && (exception is OperationCanceledException))
                {
                    _ = _completion.TrySetCanceled(_cancellationToken);
                }
                else
                {
                    _ = _completion.TrySetException(exception);
                }
            }
        }
    }

    private sealed class ReadbackBufferRequest : IGraphicsDeviceWork
    {
        private readonly nint _byteLength;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<OwnedBuffer<byte>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly GraphicsDevice _device;
        private int _nativeWorkStarted;
        private readonly GpuBuffer _source;
        private readonly long _sourceOffset;
        private GraphicsResourceOwner.UseLease? _sourceUse;
        private int _state;

        internal ReadbackBufferRequest(
            GraphicsDevice device,
            GpuBuffer source,
            GraphicsResourceOwner.UseLease sourceUse,
            long sourceOffset,
            nint byteLength,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sourceUse);
            _byteLength = byteLength;
            _cancellationToken = cancellationToken;
            _device = device;
            _source = source;
            _sourceOffset = sourceOffset;
            _sourceUse = sourceUse;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((ReadbackBufferRequest)state!).Cancel(),
                this);
        }

        internal Task<OwnedBuffer<byte>> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseSourceUse();
                CompleteCanceled();
                _cancellationRegistration.Dispose();
                return;
            }

            OwnedBuffer<byte>? readback = null;
            OwnedBuffer<byte>? completedReadback = null;
            var completeCanceled = false;

            try
            {
                Volatile.Write(ref _nativeWorkStarted, 1);
                readback = device.ReadbackBufferOnOwner(_source, _sourceOffset, _byteLength);

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    completedReadback = readback;
                    readback = null;
                }
                else
                {
                    readback.Dispose();
                    readback = null;
                    completeCanceled = true;
                }
            }
            finally
            {
                readback?.Dispose();
                ReleaseSourceUse();

                if (completedReadback is not null)
                {
                    if (!_completion.TrySetResult(completedReadback))
                    {
                        completedReadback.Dispose();
                    }
                }
                else if (completeCanceled)
                {
                    CompleteCanceled();
                }

                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            var previousState = Interlocked.Exchange(ref _state, 2);
            ReleaseSourceUse();

            if ((previousState == 1) && (Volatile.Read(ref _nativeWorkStarted) == 0))
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseSourceUse();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
            => _ = Interlocked.CompareExchange(ref _state, 1, 0);

        private void CompleteCanceled()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) == 1)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseSourceUse()
        {
            var sourceUse = Interlocked.Exchange(ref _sourceUse, null);
            sourceUse?.Dispose();
        }
    }

    private sealed class ReadbackTextureRequest : IGraphicsDeviceWork
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<TextureReadback> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly GraphicsDevice _device;
        private int _nativeWorkStarted;
        private readonly GpuTexture _source;
        private GraphicsResourceOwner.UseLease? _sourceUse;
        private int _state;
        private readonly TextureSubresource _subresource;

        internal ReadbackTextureRequest(
            GraphicsDevice device,
            GpuTexture source,
            GraphicsResourceOwner.UseLease sourceUse,
            TextureSubresource subresource,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(sourceUse);
            _cancellationToken = cancellationToken;
            _device = device;
            _source = source;
            _subresource = subresource;
            _sourceUse = sourceUse;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((ReadbackTextureRequest)state!).Cancel(),
                this);
        }

        internal Task<TextureReadback> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseSourceUse();
                CompleteCanceled();
                _cancellationRegistration.Dispose();
                return;
            }

            TextureReadback? readback = null;
            TextureReadback? completedReadback = null;
            var completeCanceled = false;

            try
            {
                Volatile.Write(ref _nativeWorkStarted, 1);
                readback = device.ReadbackTextureOnOwner(_source, _subresource);

                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
                {
                    completedReadback = readback;
                    readback = null;
                }
                else
                {
                    readback.Dispose();
                    readback = null;
                    completeCanceled = true;
                }
            }
            finally
            {
                readback?.Dispose();
                ReleaseSourceUse();

                if (completedReadback is not null)
                {
                    if (!_completion.TrySetResult(completedReadback))
                    {
                        completedReadback.Dispose();
                    }
                }
                else if (completeCanceled)
                {
                    CompleteCanceled();
                }

                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            var previousState = Interlocked.Exchange(ref _state, 2);
            ReleaseSourceUse();

            if ((previousState == 1) && (Volatile.Read(ref _nativeWorkStarted) == 0))
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseSourceUse();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
            => _ = Interlocked.CompareExchange(ref _state, 1, 0);

        private void CompleteCanceled()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) == 1)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseSourceUse()
        {
            var sourceUse = Interlocked.Exchange(ref _sourceUse, null);
            sourceUse?.Dispose();
        }
    }

    private sealed class RetireResourceRequest : IGraphicsDeviceWork
    {
        private readonly GraphicsDevice _device;
        private readonly GraphicsResourceOwner _resource;

        internal RetireResourceRequest(GraphicsDevice device, GraphicsResourceOwner resource)
        {
            _resource = resource;
            _device = device;
        }

        public void Execute(GraphicsDevice device)
            => device.RetireResourceOnOwner(_resource);

        public void Fail(Exception exception)
            => _device.QuarantineResource(_resource, exception);
    }

    private sealed class UploadBufferRequest : IGraphicsDeviceWork
    {
        private readonly nint _byteLength;
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<UploadReceipt> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly GpuBuffer _destination;
        private readonly long _destinationOffset;
        private GraphicsResourceOwner.UseLease? _destinationUse;
        private readonly GraphicsDevice _device;
        private int _nativeWorkStarted;
        private readonly byte[]? _sourceBytes;
        private BufferReadLease<byte>? _sourceLease;
        private readonly nint _sourceOffset;
        private int _state;

        internal UploadBufferRequest(
            GraphicsDevice device,
            GpuBuffer destination,
            GraphicsResourceOwner.UseLease destinationUse,
            long destinationOffset,
            byte[]? sourceBytes,
            BufferReadLease<byte>? sourceLease,
            nint sourceOffset,
            nint byteLength,
            CancellationToken cancellationToken)
        {
            if ((sourceBytes is null) == (sourceLease is null))
            {
                throw new ArgumentException("An upload request requires exactly one byte source.");
            }

            ArgumentNullException.ThrowIfNull(destinationUse);
            _byteLength = byteLength;
            _cancellationToken = cancellationToken;
            _destination = destination;
            _destinationOffset = destinationOffset;
            _device = device;
            _destinationUse = destinationUse;
            _sourceBytes = sourceBytes;
            _sourceLease = sourceLease;
            _sourceOffset = sourceOffset;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((UploadBufferRequest)state!).Cancel(),
                this);
        }

        internal Task<UploadReceipt> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseSourceLease();
                ReleaseDestinationUse();
                CompleteCanceled();
                _cancellationRegistration.Dispose();
                return;
            }

            UploadReceipt? receipt = null;
            var completeCanceled = false;

            try
            {
                Volatile.Write(ref _nativeWorkStarted, 1);

                var completedFence = (_sourceBytes is byte[] sourceBytes)
                    ? device.UploadBufferOnOwner(_destination, _destinationOffset, sourceBytes)
                    : device.UploadBufferOnOwner(
                        _destination,
                        _destinationOffset,
                        _sourceLease ?? throw new InvalidOperationException("The upload request has no retained source lease."),
                        _sourceOffset,
                        _byteLength);
                receipt = new UploadReceipt(UploadStatus.Ready, device.DeviceEpoch, completedFence, rejection: null);

                if (Interlocked.CompareExchange(ref _state, 2, 0) != 0)
                {
                    completeCanceled = true;
                    receipt = null;
                }
            }
            finally
            {
                ReleaseSourceLease();
                ReleaseDestinationUse();

                if (receipt is not null)
                {
                    _ = _completion.TrySetResult(receipt);
                }
                else if (completeCanceled)
                {
                    CompleteCanceled();
                }

                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            var previousState = Interlocked.Exchange(ref _state, 2);
            ReleaseSourceLease();
            ReleaseDestinationUse();

            if ((previousState == 1) && (Volatile.Read(ref _nativeWorkStarted) == 0))
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseSourceLease();
            ReleaseDestinationUse();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
            => _ = Interlocked.CompareExchange(ref _state, 1, 0);

        private void CompleteCanceled()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) == 1)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseDestinationUse()
        {
            var destinationUse = Interlocked.Exchange(ref _destinationUse, null);
            destinationUse?.Dispose();
        }

        private void ReleaseSourceLease()
        {
            var sourceLease = Interlocked.Exchange(ref _sourceLease, null);
            sourceLease?.Dispose();
        }
    }

    private sealed class UploadTextureRequest : IGraphicsDeviceWork
    {
        private readonly CancellationTokenRegistration _cancellationRegistration;
        private readonly CancellationToken _cancellationToken;
        private readonly TextureDataLayout _sourceLayout;
        private readonly nint _sourceOffset;
        private readonly TaskCompletionSource<UploadReceipt> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly GpuTexture _destination;
        private GraphicsResourceOwner.UseLease? _destinationUse;
        private readonly GraphicsDevice _device;
        private int _nativeWorkStarted;
        private BufferReadLease<byte>? _sourceLease;
        private int _state;
        private readonly TextureSubresource _subresource;

        internal UploadTextureRequest(
            GraphicsDevice device,
            GpuTexture destination,
            GraphicsResourceOwner.UseLease destinationUse,
            TextureSubresource subresource,
            BufferReadLease<byte> sourceLease,
            nint sourceOffset,
            TextureDataLayout sourceLayout,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destinationUse);
            ArgumentNullException.ThrowIfNull(sourceLease);
            _cancellationToken = cancellationToken;
            _destination = destination;
            _destinationUse = destinationUse;
            _device = device;
            _sourceLease = sourceLease;
            _sourceLayout = sourceLayout;
            _sourceOffset = sourceOffset;
            _subresource = subresource;
            _cancellationRegistration = cancellationToken.Register(
                static state => ((UploadTextureRequest)state!).Cancel(),
                this);
        }

        internal Task<UploadReceipt> Task => _completion.Task;

        public void Execute(GraphicsDevice device)
        {
            if (Volatile.Read(ref _state) == 1)
            {
                ReleaseSourceLease();
                ReleaseDestinationUse();
                CompleteCanceled();
                _cancellationRegistration.Dispose();
                return;
            }

            UploadReceipt? receipt = null;
            var completeCanceled = false;

            try
            {
                Volatile.Write(ref _nativeWorkStarted, 1);
                var completedFence = device.UploadTextureOnOwner(
                    _destination,
                    _subresource,
                    _sourceLease ?? throw new InvalidOperationException("The texture upload request has no retained source lease."),
                    _sourceOffset,
                    _sourceLayout);
                receipt = new UploadReceipt(UploadStatus.Ready, device.DeviceEpoch, completedFence, rejection: null);

                if (Interlocked.CompareExchange(ref _state, 2, 0) != 0)
                {
                    completeCanceled = true;
                    receipt = null;
                }
            }
            finally
            {
                ReleaseSourceLease();
                ReleaseDestinationUse();

                if (receipt is not null)
                {
                    _ = _completion.TrySetResult(receipt);
                }
                else if (completeCanceled)
                {
                    CompleteCanceled();
                }

                _cancellationRegistration.Dispose();
            }
        }

        public void Fail(Exception exception)
        {
            if (exception is GraphicsDeviceLostException)
            {
                _device.MarkDeviceLost();
            }

            var previousState = Interlocked.Exchange(ref _state, 2);
            ReleaseSourceLease();
            ReleaseDestinationUse();

            if ((previousState == 1) && (Volatile.Read(ref _nativeWorkStarted) == 0))
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
            else
            {
                _ = _completion.TrySetException(exception);
            }

            _cancellationRegistration.Dispose();
        }

        internal void AdmissionFailed()
        {
            _ = Interlocked.Exchange(ref _state, 2);
            ReleaseSourceLease();
            ReleaseDestinationUse();
            _cancellationRegistration.Dispose();
        }

        private void Cancel()
            => _ = Interlocked.CompareExchange(ref _state, 1, 0);

        private void CompleteCanceled()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 1) == 1)
            {
                _ = _completion.TrySetCanceled(_cancellationToken);
            }
        }

        private void ReleaseDestinationUse()
        {
            var destinationUse = Interlocked.Exchange(ref _destinationUse, null);
            destinationUse?.Dispose();
        }

        private void ReleaseSourceLease()
        {
            var sourceLease = Interlocked.Exchange(ref _sourceLease, null);
            sourceLease?.Dispose();
        }
    }
}
