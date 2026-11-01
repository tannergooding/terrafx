// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading;
using TerraFX;

namespace TerraFX.Platform;

internal sealed class HeadlessPlatformBackend : IPlatformBackend
{
    private readonly ClipboardServiceStatus _clipboardStatus;
    private int _isDisposed;
    private readonly uint _ownerId;
    private readonly int _ownerThreadId;

    internal HeadlessPlatformBackend(uint ownerId, ClipboardServicePolicy clipboardPolicy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(ownerId);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)clipboardPolicy, (int)ClipboardServicePolicy.Required);
        ArgumentOutOfRangeException.ThrowIfNegative((int)clipboardPolicy);

        if (clipboardPolicy == ClipboardServicePolicy.Required)
        {
            throw new NotSupportedException("The headless platform does not provide clipboard access.");
        }

        _ownerId = ownerId;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _clipboardStatus = clipboardPolicy == ClipboardServicePolicy.Optional
            ? ClipboardServiceStatus.Unsupported
            : ClipboardServiceStatus.NotRequested;
    }

    public PlatformServiceStatus AudioStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.NotRequested;
        }
    }

    public PlatformBackend Backend
    {
        get
        {
            EnsureOwnerThread();
            return PlatformBackend.Headless;
        }
    }

    public ClipboardServiceStatus ClipboardStatus
    {
        get
        {
            EnsureOwnerThread();
            return _clipboardStatus;
        }
    }

    public PlatformServiceStatus DisplayStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.NotRequested;
        }
    }

    public uint OwnerId => _ownerId;

    public PlatformServiceStatus PhysicalInputStatus
    {
        get
        {
            EnsureOwnerThread();
            return PlatformServiceStatus.NotRequested;
        }
    }

    public void CloseWindow(WindowHandle window)
    {
        EnsureOwnerThread();
        _ = window;
        throw new NotSupportedException("The Headless platform does not create windows.");
    }

    public void CreateWindow(WindowHandle window, WindowOptions options, nint nativeMonitorHandle)
    {
        EnsureOwnerThread();
        _ = window;
        _ = options;
        _ = nativeMonitorHandle;
        throw new NotSupportedException("The Headless platform does not create windows.");
    }

    public void Dispose()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        EnsureOwnerThread();
        _ = Interlocked.Exchange(ref _isDisposed, 1);
    }

    public ImmutableArray<WindowHandle> DrainWindowCloseRequests()
    {
        EnsureOwnerThread();
        return [];
    }

    public ImmutableArray<MonitorHandle> GetMonitorHandles()
    {
        EnsureOwnerThread();
        return [];
    }

    public ImmutableArray<PlatformMonitorInfo> GetMonitorInfo()
    {
        EnsureOwnerThread();
        return [];
    }

    public void Pump()
    {
        EnsureOwnerThread();
    }

    public void SetMonitorHandles(ImmutableArray<MonitorHandle> monitorHandles)
    {
        EnsureOwnerThread();

        if (monitorHandles.IsDefaultOrEmpty)
        {
            return;
        }

        throw new ArgumentException("The Headless platform has no monitor handles.", nameof(monitorHandles));
    }

    public bool TakeMonitorTopologyChanged()
    {
        EnsureOwnerThread();
        return false;
    }

    public bool TryGetNativeWindowBinding(WindowHandle window, out PlatformNativeWindowBinding binding)
    {
        EnsureOwnerThread();
        _ = window;
        binding = default;
        return false;
    }

    private void EnsureOwnerThread()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);

        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The Platform backend must be accessed on its host-owner thread.");
        }
    }
}
