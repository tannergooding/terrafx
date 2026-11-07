// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Runtime.InteropServices;

namespace TerraFX.Platform;

internal static partial class LinuxEvdevGamepadDiscovery
{
    private const string SystemdLibrary = "libsystemd.so.0";

    internal static ImmutableArray<LinuxEvdevGamepadDevice> Enumerate()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux evdev device discovery requires Linux.");
        }

        var result = SystemdNative.DeviceEnumeratorNew(out var enumerator);
        EnsureSuccess(result, "create a device enumerator");

        if (enumerator == 0)
        {
            throw new InvalidOperationException("systemd returned no device enumerator.");
        }

        try
        {
            EnsureSuccess(
                SystemdNative.DeviceEnumeratorAddMatchSubsystem(enumerator, "input", match: 1),
                "select the input subsystem");

            var devices = new List<LinuxEvdevGamepadDevice>();
            var device = SystemdNative.DeviceEnumeratorGetFirst(enumerator);

            while (device != 0)
            {
                if (TryGetGamepadNode(device, out var gamepadDevice))
                {
                    devices.Add(gamepadDevice);
                }

                device = SystemdNative.DeviceEnumeratorGetNext(enumerator);
            }

            devices.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.SysPath, right.SysPath));
            return [.. devices];
        }
        finally
        {
            _ = SystemdNative.DeviceEnumeratorUnref(enumerator);
        }
    }

    private static void EnsureSuccess(int result, string operation)
    {
        if (result < 0)
        {
            throw new IOException($"systemd could not {operation}; status {result}.");
        }
    }

    private static bool TryGetGamepadNode(
        nint device,
        out LinuxEvdevGamepadDevice gamepadDevice)
    {
        gamepadDevice = default;
        var propertyResult = SystemdNative.DeviceGetPropertyValue(device, "ID_INPUT_JOYSTICK", out var propertyValue);

        if (propertyResult == -2)
        {
            return false;
        }

        EnsureSuccess(propertyResult, "read the joystick classification");

        if ((propertyValue == 0)
            || !string.Equals(
                Marshal.PtrToStringUTF8(propertyValue),
                "1",
                StringComparison.Ordinal))
        {
            return false;
        }

        var sysPathResult = SystemdNative.DeviceGetSysPath(device, out var sysPath);
        EnsureSuccess(sysPathResult, "read the input-device sysfs path");
        var sysPathText = Marshal.PtrToStringUTF8(sysPath)
            ?? throw new InvalidDataException("systemd returned an invalid input-device sysfs path.");
        var deviceNodeResult = SystemdNative.DeviceGetDevName(device, out var deviceNode);

        if (deviceNodeResult == -2)
        {
            return false;
        }

        EnsureSuccess(deviceNodeResult, "read the gamepad device node");
        var deviceNodeText = Marshal.PtrToStringUTF8(deviceNode)
            ?? throw new InvalidDataException("systemd returned an invalid gamepad device node.");

        if (!Path.IsPathRooted(deviceNodeText)
            || !deviceNodeText.StartsWith("/dev/input/event", StringComparison.Ordinal))
        {
            return false;
        }

        gamepadDevice = new LinuxEvdevGamepadDevice(sysPathText, deviceNodeText);
        return true;
    }

    private static partial class SystemdNative
    {
        [LibraryImport(SystemdLibrary, EntryPoint = "sd_device_enumerator_add_match_subsystem", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int DeviceEnumeratorAddMatchSubsystem(nint enumerator, string subsystem, int match);

        [LibraryImport(SystemdLibrary, EntryPoint = "sd_device_enumerator_get_device_first")]
        internal static partial nint DeviceEnumeratorGetFirst(nint enumerator);

        [LibraryImport(SystemdLibrary, EntryPoint = "sd_device_enumerator_get_device_next")]
        internal static partial nint DeviceEnumeratorGetNext(nint enumerator);

        [LibraryImport(SystemdLibrary, EntryPoint = "sd_device_enumerator_new")]
        internal static partial int DeviceEnumeratorNew(out nint enumerator);

        [LibraryImport(SystemdLibrary, EntryPoint = "sd_device_enumerator_unref")]
        internal static partial nint DeviceEnumeratorUnref(nint enumerator);

        [LibraryImport(SystemdLibrary, EntryPoint = "sd_device_get_devname")]
        internal static partial int DeviceGetDevName(nint device, out nint devname);

        [LibraryImport(SystemdLibrary, EntryPoint = "sd_device_get_property_value", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int DeviceGetPropertyValue(nint device, string property, out nint value);

        [LibraryImport(SystemdLibrary, EntryPoint = "sd_device_get_syspath")]
        internal static partial int DeviceGetSysPath(nint device, out nint syspath);
    }
}
