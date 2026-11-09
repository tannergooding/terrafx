// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace TerraFX.Platform;

[SuppressMessage(
    "Reliability",
    "CA2216:Disposable types should declare finalizer",
    Justification = "The native protocol metadata must remain alive through owner-thread proxy teardown.")]
internal sealed unsafe class WaylandProtocolMetadata : IDisposable
{
    private readonly List<nint> _allocations;
    private bool _isDisposed;
    private readonly List<nint> _strings;
    private nint _wlDataDeviceInterface;
    private nint _wlDataDeviceManagerInterface;
    private nint _wlDataOfferInterface;
    private nint _wlDataSourceInterface;
    private nint _wlKeyboardInterface;
    private nint _wlOutputInterface;
    private nint _wlPointerInterface;
    private nint _wlSeatInterface;
    private nint _wlSurfaceInterface;
    private nint _xdgPopupInterface;
    private nint _xdgPositionerInterface;
    private nint _xdgSurfaceInterface;
    private nint _xdgToplevelInterface;
    private nint _xdgWmBaseInterface;

    private WaylandProtocolMetadata()
    {
        _allocations = [];
        _strings = [];
    }

    internal nint WlDataDeviceInterface => _wlDataDeviceInterface;

    internal nint WlDataDeviceManagerInterface => _wlDataDeviceManagerInterface;

    internal nint WlDataOfferInterface => _wlDataOfferInterface;

    internal nint WlDataSourceInterface => _wlDataSourceInterface;

    internal nint WlKeyboardInterface => _wlKeyboardInterface;

    internal nint WlOutputInterface => _wlOutputInterface;

    internal nint WlPointerInterface => _wlPointerInterface;

    internal nint WlSeatInterface => _wlSeatInterface;

    internal nint WlSurfaceInterface => _wlSurfaceInterface;

    internal nint XdgSurfaceInterface => _xdgSurfaceInterface;

    internal nint XdgToplevelInterface => _xdgToplevelInterface;

    internal nint XdgWmBaseInterface => _xdgWmBaseInterface;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "Partially allocated protocol metadata must be released if construction fails.")]
    internal static WaylandProtocolMetadata Create(WaylandDisplayConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var metadata = new WaylandProtocolMetadata();

        try
        {
            metadata.Initialize(connection);
            return metadata;
        }
        catch (Exception exception)
        {
            try
            {
                metadata.Dispose();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Wayland protocol metadata setup and cleanup both failed.", exception, cleanupException);
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        for (var index = _allocations.Count - 1; index >= 0; index--)
        {
            NativeMemory.Free((void*)_allocations[index]);
        }

        for (var index = _strings.Count - 1; index >= 0; index--)
        {
            Marshal.FreeCoTaskMem(_strings[index]);
        }

        _allocations.Clear();
        _strings.Clear();
        _isDisposed = true;
    }

    private nint AllocateArray<T>(int length)
        where T : unmanaged
    {
        if (length == 0)
        {
            return 0;
        }

        var allocation = NativeMemory.AllocZeroed(checked((nuint)length), checked((nuint)sizeof(T)));

        if (allocation is null)
        {
            throw new InvalidOperationException("The native Wayland protocol metadata allocation failed.");
        }

        _allocations.Add((nint)allocation);
        return (nint)allocation;
    }

    private nint AllocateInterface(string name, int version)
    {
        var interfacePointer = (NativeInterface*)AllocateArray<NativeInterface>(1);
        interfacePointer->Name = AllocateString(name);
        interfacePointer->Version = version;
        return (nint)interfacePointer;
    }

    private nint AllocateString(string value)
    {
        var stringPointer = Marshal.StringToCoTaskMemUTF8(value);
        _strings.Add(stringPointer);
        return stringPointer;
    }

    private nint CreateMessages(MessageDefinition[] definitions)
    {
        var messages = (NativeMessage*)AllocateArray<NativeMessage>(definitions.Length);

        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            messages[index].Name = AllocateString(definition.Name);
            messages[index].Signature = AllocateString(definition.Signature);
            messages[index].Types = CreateTypes(definition.Types);
        }

        return (nint)messages;
    }

    private nint CreateTypes(nint[]? types)
    {
        if (types is null)
        {
            return 0;
        }

        var typeArray = (nint*)AllocateArray<nint>(types.Length);

        for (var index = 0; index < types.Length; index++)
        {
            typeArray[index] = types[index];
        }

        return (nint)typeArray;
    }

    private void Initialize(WaylandDisplayConnection connection)
    {
        _wlDataDeviceInterface = connection.GetInterface("wl_data_device_interface");
        _wlDataDeviceManagerInterface = connection.GetInterface("wl_data_device_manager_interface");
        _wlDataOfferInterface = connection.GetInterface("wl_data_offer_interface");
        _wlDataSourceInterface = connection.GetInterface("wl_data_source_interface");
        _wlKeyboardInterface = connection.GetInterface("wl_keyboard_interface");
        _wlOutputInterface = connection.GetInterface("wl_output_interface");
        _wlPointerInterface = connection.GetInterface("wl_pointer_interface");
        _wlSeatInterface = connection.GetInterface("wl_seat_interface");
        _wlSurfaceInterface = connection.GetInterface("wl_surface_interface");
        _xdgPositionerInterface = AllocateInterface("xdg_positioner", version: 1);
        _xdgPopupInterface = AllocateInterface("xdg_popup", version: 1);
        _xdgSurfaceInterface = AllocateInterface("xdg_surface", version: 1);
        _xdgToplevelInterface = AllocateInterface("xdg_toplevel", version: 1);
        _xdgWmBaseInterface = AllocateInterface("xdg_wm_base", version: 1);

        InitializeInterface(
            _xdgPositionerInterface,
            [
                new("destroy", ""),
                new("set_size", "ii"),
                new("set_anchor_rect", "iiii"),
                new("set_anchor", "u"),
                new("set_gravity", "u"),
                new("set_constraint_adjustment", "u"),
                new("set_offset", "ii"),
            ],
            []);
        InitializeInterface(
            _xdgPopupInterface,
            [
                new("destroy", ""),
                new("grab", "ou", [_wlSeatInterface]),
            ],
            [
                new("configure", "iiii"),
                new("popup_done", ""),
            ]);
        InitializeInterface(
            _xdgSurfaceInterface,
            [
                new("destroy", ""),
                new("get_toplevel", "n", [_xdgToplevelInterface]),
                new("get_popup", "n?oo", [_xdgPopupInterface, _xdgSurfaceInterface, _xdgPositionerInterface]),
                new("set_window_geometry", "iiii"),
                new("ack_configure", "u"),
            ],
            [
                new("configure", "u"),
            ]);
        InitializeInterface(
            _xdgToplevelInterface,
            [
                new("destroy", ""),
                new("set_parent", "?o", [_xdgToplevelInterface]),
                new("set_title", "s"),
                new("set_app_id", "s"),
                new("show_window_menu", "ouii", [_wlSeatInterface]),
                new("move", "ou", [_wlSeatInterface]),
                new("resize", "ouu", [_wlSeatInterface]),
                new("set_max_size", "ii"),
                new("set_min_size", "ii"),
                new("set_maximized", ""),
                new("unset_maximized", ""),
                new("set_fullscreen", "?o", [_wlOutputInterface]),
                new("unset_fullscreen", ""),
                new("set_minimized", ""),
            ],
            [
                new("configure", "iia"),
                new("close", ""),
            ]);
        InitializeInterface(
            _xdgWmBaseInterface,
            [
                new("destroy", ""),
                new("create_positioner", "n", [_xdgPositionerInterface]),
                new("get_xdg_surface", "no", [_xdgSurfaceInterface, _wlSurfaceInterface]),
                new("pong", "u"),
            ],
            [
                new("ping", "u"),
            ]);
    }

    private void InitializeInterface(
        nint interfacePointer,
        MessageDefinition[] requests,
        MessageDefinition[] events)
    {
        var interfaceDefinition = (NativeInterface*)interfacePointer;
        interfaceDefinition->MethodCount = requests.Length;
        interfaceDefinition->Methods = CreateMessages(requests);
        interfaceDefinition->EventCount = events.Length;
        interfaceDefinition->Events = CreateMessages(events);
    }

    private readonly record struct MessageDefinition(string Name, string Signature, nint[]? Types = null);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInterface
    {
        internal nint Name;
        internal int Version;
        internal int MethodCount;
        internal nint Methods;
        internal int EventCount;
        internal nint Events;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint Name;
        internal nint Signature;
        internal nint Types;
    }
}
