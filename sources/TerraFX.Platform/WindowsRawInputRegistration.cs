// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using TerraFX.Interop.Windows;

namespace TerraFX.Platform;

[SupportedOSPlatform("windows6.1")]
internal sealed unsafe partial class WindowsRawInputRegistration : IDisposable
{
    private const ushort GenericDesktopUsagePage = 0x0001;
    private const ushort KeyboardUsage = 0x0006;
    private const ushort MouseUsage = 0x0002;
    private const uint RawInputDeviceNotify = 0x00002000;
    private const uint RawInputDeviceRemove = 0x00000001;
    private static WindowsRawInputRegistration? s_activeRegistration;
    private static readonly Lock s_gate = new();
    private static bool s_isAdmissionReserved;
    private static WindowsPlatformOwnerInitialization? s_quarantinedOwner;
    private int _isDisposed;
    private readonly WindowsPlatformOwnerInitialization _platformOwner;

    private WindowsRawInputRegistration(WindowsPlatformOwnerInitialization platformOwner)
    {
        _platformOwner = platformOwner;
    }

    internal static WindowsRawInputRegistration Register(WindowsPlatformOwnerInitialization platformOwner)
    {
        ArgumentNullException.ThrowIfNull(platformOwner);
        var registration = new WindowsRawInputRegistration(platformOwner);
        platformOwner.ReserveRawInputRegistration();

        lock (s_gate)
        {
            if (s_isAdmissionReserved)
            {
                platformOwner.ReleaseRawInputRegistration();
                throw s_quarantinedOwner is null
                    ? new NativeHostAlreadyActiveException()
                    : new NativeHostAlreadyActiveException("NativeHostAlreadyActive; a prior Raw Input rollback remains quarantined.");
            }

            s_isAdmissionReserved = true;
        }

        if (!ConfigureRawInputDevices(RawInputDeviceNotify))
        {
            var registrationError = new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "The Windows Platform owner could not register keyboard and mouse Raw Input.");

            if (ConfigureRawInputDevices(RawInputDeviceRemove))
            {
                lock (s_gate)
                {
                    s_isAdmissionReserved = false;
                }

                platformOwner.ReleaseRawInputRegistration();
                throw registrationError;
            }

            lock (s_gate)
            {
                s_quarantinedOwner = platformOwner;
            }

            throw new AggregateException(
                registrationError,
                new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "The Windows Platform owner could not prove Raw Input registration rollback; native-host admission remains quarantined."));
        }

        lock (s_gate)
        {
            s_activeRegistration = registration;
        }

        return registration;
    }

    private static bool ConfigureRawInputDevices(uint flags)
    {
        var devices = stackalloc RAWINPUTDEVICE[] {
            new RAWINPUTDEVICE {
                usUsagePage = GenericDesktopUsagePage,
                usUsage = KeyboardUsage,
                dwFlags = flags,
                hwndTarget = default,
            },
            new RAWINPUTDEVICE {
                usUsagePage = GenericDesktopUsagePage,
                usUsage = MouseUsage,
                dwFlags = flags,
                hwndTarget = default,
            },
        };
        return RegisterRawInputDevicesNative(devices, 2, (uint)sizeof(RAWINPUTDEVICE));
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterRawInputDevices", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevicesNative(RAWINPUTDEVICE* devices, uint deviceCount, uint size);

    public void Dispose()
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return;
        }

        _platformOwner.EnsureOwnerThread();

        lock (s_gate)
        {
            if (!ReferenceEquals(s_activeRegistration, this))
            {
                throw new InvalidOperationException("Raw Input registration ownership changed before retirement.");
            }
        }

        if (!ConfigureRawInputDevices(RawInputDeviceRemove))
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                "The Windows Platform owner could not unregister keyboard and mouse Raw Input.");
        }

        _platformOwner.ReleaseRawInputRegistration();

        lock (s_gate)
        {
            s_activeRegistration = null;
            s_isAdmissionReserved = false;
            s_quarantinedOwner = null;
        }

        _ = Interlocked.Exchange(ref _isDisposed, 1);
    }
}
