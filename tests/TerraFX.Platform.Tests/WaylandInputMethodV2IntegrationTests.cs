// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform.Tests;

[TestFixture]
internal sealed unsafe partial class WaylandInputMethodV2IntegrationTests
{
    private const uint KeymapFormatXkbV1 = 1;
    private const uint LeftShiftEvdevKey = 42;
    private const string WindowTitle = "TerraFX Wayland input-method probe";
    private const int XkbKeymapFormatTextV1 = 1;

    private static nint AllocateListenerTable(nint callback)
    {
        var listener = (nint*)NativeMemory.AllocZeroed(1, checked((nuint)sizeof(nint)));

        if (listener is null)
        {
            throw new InvalidOperationException("The Wayland test listener allocation failed.");
        }

        listener[0] = callback;
        return (nint)listener;
    }

    private static void AttachSharedBuffer(
        WaylandDisplayConnection connection,
        nint surface,
        ref nint sharedMemoryListener,
        ref nint sharedBufferListener)
    {
        var sharedMemory = connection.BindGlobal("wl_shm", maximumVersion: 1);
        sharedMemoryListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnSharedMemoryFormat);

        if (connection.AddListener(sharedMemory, sharedMemoryListener, data: 0) < 0)
        {
            throw new IOException("The Wayland shared-memory format listener could not be installed.");
        }

        connection.Roundtrip();
        var poolInterface = connection.GetInterface("wl_shm_pool_interface");
        var bufferInterface = connection.GetInterface("wl_buffer_interface");
        var pixels = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };
        using var sharedFile = CreateTemporaryFile(pixels);
        var poolArguments = stackalloc WaylandArgument[3];
        poolArguments[0] = default;
        poolArguments[1].FileDescriptor = sharedFile.SafeFileHandle.DangerousGetHandle().ToInt32();
        poolArguments[2].FileDescriptor = pixels.Length;
        var pool = connection.CreateProxy(
            sharedMemory,
            opcode: 0,
            poolInterface,
            version: 1,
            poolArguments);

        if (pool == 0)
        {
            throw new IOException("The Wayland compositor could not create the shared-memory pool.");
        }

        var bufferArguments = stackalloc WaylandArgument[6];
        bufferArguments[0] = default;
        bufferArguments[1].FileDescriptor = 0;
        bufferArguments[2].FileDescriptor = 1;
        bufferArguments[3].FileDescriptor = 1;
        bufferArguments[4].FileDescriptor = 4;
        bufferArguments[5].Unsigned = 0;
        var buffer = connection.CreateProxy(
            pool,
            opcode: 0,
            bufferInterface,
            version: 1,
            bufferArguments);

        if (buffer == 0)
        {
            throw new IOException("The Wayland shared-memory pool could not create a buffer.");
        }

        sharedBufferListener = AllocateListenerTable(
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnSharedBufferRelease);

        if (connection.AddListener(buffer, sharedBufferListener, data: 0) < 0)
        {
            throw new IOException("The Wayland shared-memory buffer listener could not be installed.");
        }

        var attachArguments = stackalloc WaylandArgument[3];
        attachArguments[0].Pointer = buffer;
        attachArguments[1].FileDescriptor = 0;
        attachArguments[2].FileDescriptor = 0;
        connection.SendRequest(surface, opcode: 1, attachArguments);
        var damageArguments = stackalloc WaylandArgument[4];
        damageArguments[0].FileDescriptor = 0;
        damageArguments[1].FileDescriptor = 0;
        damageArguments[2].FileDescriptor = 1;
        damageArguments[3].FileDescriptor = 1;
        connection.SendRequest(surface, opcode: 2, damageArguments);
        connection.SendRequest(surface, opcode: 6);
        connection.Roundtrip();
    }

    private static bool ContainsFocus(List<PlatformInputSample> samples, WindowHandle window)
    {
        foreach (var sample in samples)
        {
            if ((sample.Kind == PhysicalInputEventKind.FocusGained)
                && (sample.Window == window))
            {
                return true;
            }
        }

        return false;
    }

    private static FileStream CreateTemporaryFile(ReadOnlySpan<byte> content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrafx-wayland-ime-{Guid.NewGuid():N}.tmp");
        var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            bufferSize: 4096,
            FileOptions.DeleteOnClose);
        stream.Write(content);
        stream.Flush();
        return stream;
    }

    private static byte[] CreateXkbKeymap()
    {
        var context = XkbNative.ContextNew(flags: 0);

        if (context == 0)
        {
            throw new InvalidOperationException("libxkbcommon could not create an XKB context.");
        }

        nint rules = 0;
        nint model = 0;
        nint layout = 0;
        nint keymap = 0;
        nint keymapText = 0;

        try
        {
            rules = Marshal.StringToCoTaskMemUTF8("evdev");
            model = Marshal.StringToCoTaskMemUTF8("pc105");
            layout = Marshal.StringToCoTaskMemUTF8("us");
            var names = new XkbRuleNames {
                Rules = rules,
                Model = model,
                Layout = layout,
            };
            keymap = XkbNative.KeymapNewFromNames(context, &names, flags: 0);

            if (keymap == 0)
            {
                throw new InvalidOperationException("libxkbcommon could not create a US keymap.");
            }

            keymapText = XkbNative.KeymapGetAsString(keymap, XkbKeymapFormatTextV1);

            if (keymapText == 0)
            {
                throw new InvalidOperationException("libxkbcommon could not serialize the US keymap.");
            }

            var text = Marshal.PtrToStringUTF8(keymapText)
                ?? throw new InvalidOperationException("libxkbcommon returned a null keymap string.");
            return Encoding.UTF8.GetBytes(text + '\0');
        }
        finally
        {
            if (keymapText != 0)
            {
                XkbNative.Free(keymapText);
            }

            if (keymap != 0)
            {
                XkbNative.KeymapUnref(keymap);
            }

            if (rules != 0)
            {
                Marshal.FreeCoTaskMem(rules);
            }

            if (model != 0)
            {
                Marshal.FreeCoTaskMem(model);
            }

            if (layout != 0)
            {
                Marshal.FreeCoTaskMem(layout);
            }

            XkbNative.ContextUnref(context);
        }
    }

    private static void DrainSamples(
        WaylandPlatformBackend backend,
        List<PlatformInputSample> inputSamples,
        List<PlatformTextInputSample> textSamples)
    {
        var batch = backend.DrainInputSamples();

        if (!batch.Samples.IsDefaultOrEmpty)
        {
            inputSamples.AddRange(batch.Samples);
        }

        if (!batch.TextSamples.IsDefaultOrEmpty)
        {
            textSamples.AddRange(batch.TextSamples);
        }
    }

    private static void FocusSwayWindow(string title)
    {
        var startInfo = new ProcessStartInfo("swaymsg") {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add($"[title=\"{title}\"]");
        startInfo.ArgumentList.Add("focus");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The private Sway focus command could not start.");

        if (!process.WaitForExit(milliseconds: 5_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The private Sway focus command did not complete.");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        using var response = JsonDocument.Parse(output);
        var commands = response.RootElement;

        if ((process.ExitCode != 0)
            || (commands.ValueKind != JsonValueKind.Array)
            || (commands.GetArrayLength() != 1)
            || !commands[0].GetProperty("success").GetBoolean())
        {
            throw new InvalidOperationException($"The private Sway focus command failed: {output} {error}");
        }
    }

    private static void FreeListener(nint listener)
    {
        if (listener != 0)
        {
            NativeMemory.Free((void*)listener);
        }
    }

    private static bool IsInputMethodReady(
        InputMethodState state,
        List<PlatformInputSample> inputSamples,
        WindowHandle window)
    {
        if (state.IsUnavailable)
        {
            throw new InvalidOperationException("The private Sway input method became unavailable.");
        }

        return state.IsActive
            && (state.DoneCount > 0)
            && ContainsFocus(inputSamples, window);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInputMethodActivate(nint data, nint inputMethod)
    {
        _ = inputMethod;

        if (GCHandle.FromIntPtr(data).Target is InputMethodState state)
        {
            state.IsActive = true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInputMethodContentType(nint data, nint inputMethod, uint hint, uint purpose)
    {
        _ = data;
        _ = inputMethod;
        _ = hint;
        _ = purpose;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInputMethodDeactivate(nint data, nint inputMethod)
    {
        _ = inputMethod;

        if (GCHandle.FromIntPtr(data).Target is InputMethodState state)
        {
            state.IsActive = false;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInputMethodDone(nint data, nint inputMethod)
    {
        _ = inputMethod;

        if (GCHandle.FromIntPtr(data).Target is InputMethodState state)
        {
            state.DoneCount = unchecked(state.DoneCount + 1);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInputMethodSurroundingText(nint data, nint inputMethod, nint text, uint cursor, uint anchor)
    {
        _ = data;
        _ = inputMethod;
        _ = text;
        _ = cursor;
        _ = anchor;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInputMethodTextChangeCause(nint data, nint inputMethod, uint cause)
    {
        _ = data;
        _ = inputMethod;
        _ = cause;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInputMethodUnavailable(nint data, nint inputMethod)
    {
        _ = inputMethod;

        if (GCHandle.FromIntPtr(data).Target is InputMethodState state)
        {
            state.IsUnavailable = true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSharedBufferRelease(nint data, nint buffer)
    {
        _ = data;
        _ = buffer;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSharedMemoryFormat(nint data, nint sharedMemory, uint format)
    {
        _ = data;
        _ = sharedMemory;
        _ = format;
    }

    private static void PumpUntil(
        WaylandPlatformBackend backend,
        List<PlatformInputSample> inputSamples,
        List<PlatformTextInputSample> textSamples,
        Func<bool> condition,
        string failureMessage)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            backend.Pump();
            DrainSamples(backend, inputSamples, textSamples);

            if (condition())
            {
                return;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException(failureMessage);
    }

    private static void SetSwayOutputScale()
    {
        var startInfo = new ProcessStartInfo("swaymsg") {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("output");
        startInfo.ArgumentList.Add("*");
        startInfo.ArgumentList.Add("scale");
        startInfo.ArgumentList.Add("1.25");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The private Sway scale command could not start.");

        if (!process.WaitForExit(milliseconds: 5_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The private Sway scale command did not complete.");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        using var response = JsonDocument.Parse(output);
        var commands = response.RootElement;
        var succeeded = (process.ExitCode == 0)
            && (commands.ValueKind == JsonValueKind.Array)
            && (commands.GetArrayLength() > 0);

        for (var index = 0; succeeded && (index < commands.GetArrayLength()); index++)
        {
            succeeded = commands[index].GetProperty("success").GetBoolean();
        }

        if (!succeeded)
        {
            throw new InvalidOperationException($"The private Sway fractional-scale request failed: {output} {error}");
        }
    }

    private static bool TryFindTextChange(
        List<PlatformTextInputSample> samples,
        TextInputChangeKind kind,
        string? text,
        out PlatformTextInputSample sampleResult,
        out TextInputChange change)
    {
        foreach (var sample in samples)
        {
            foreach (var candidate in sample.Changes)
            {
                if ((candidate.Kind == kind)
                    && ((text is null) || StringComparer.Ordinal.Equals(candidate.Text, text)))
                {
                    sampleResult = sample;
                    change = candidate;
                    return true;
                }
            }
        }

        sampleResult = default;
        change = default;
        return false;
    }

    [Test]
    public void ReportsFractionalScaleAndViewportMetricsOnPrivateSway()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_WAYLAND_WSI") != "1")
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWAYSOCK")))
        {
            Assert.Ignore("The fractional-scale witness requires the private Sway test environment.");
            return;
        }

        SetSwayOutputScale();
        var backendOwner = PlatformBackendFactory.Create(
            PlatformBackend.WaylandOnly,
            ownerId: 1,
            ClipboardServicePolicy.NotRequested);
        nint sharedMemoryListener = 0;
        nint sharedBufferListener = 0;

        try
        {
            using (backendOwner)
            {
                if (backendOwner is not WaylandPlatformBackend backend)
                {
                    Assert.Fail("The Wayland backend factory returned a different backend.");
                    return;
                }

                Assert.That(backend.Connection.GetGlobals("wp_fractional_scale_manager_v1"), Is.Not.Empty);
                Assert.That(backend.Connection.GetGlobals("wp_viewporter"), Is.Not.Empty);
                var window = new WindowHandle(ownerId: 1, index: 0, generation: 1);
                var windowCreated = false;

                try
                {
                    backend.CreateWindow(
                        window,
                        new WindowOptions("TerraFX Wayland fractional-scale probe") {
                            ClientSize = new Vector2(640.0f, 360.0f),
                            MinimumClientSize = new Vector2(320.0f, 180.0f),
                            Resizable = false,
                            Visible = true,
                        },
                        nativeMonitorHandle: 0);
                    windowCreated = true;

                    if (!backend.TryGetNativeWindowBinding(window, out var binding))
                    {
                        throw new InvalidOperationException("The private Wayland backend omitted its test window binding.");
                    }

                    AttachSharedBuffer(
                        backend.Connection,
                        binding.NativeWindowOrSurface,
                        ref sharedMemoryListener,
                        ref sharedBufferListener);

                    var inputSamples = new List<PlatformInputSample>();
                    var textSamples = new List<PlatformTextInputSample>();
                    PumpUntil(
                        backend,
                        inputSamples,
                        textSamples,
                        () => backend.GetNativeWindowMetrics(window).DpiSource is DpiSource.WaylandFractionalScale,
                        "The private Sway preferred fractional scale did not reach the mapped window.");

                    var metrics = backend.GetNativeWindowMetrics(window);
                    Assert.That(metrics.ContentScale.X, Is.EqualTo(1.25f));
                    Assert.That(metrics.ContentScale.Y, Is.EqualTo(1.25f));
                    Assert.That(metrics.EffectiveDpi, Is.EqualTo(120.0f));
                    Assert.That(metrics.DpiConfidence, Is.EqualTo(DpiConfidence.Reported));
                    Assert.That(
                        metrics.ClientPixelSize.Width,
                        Is.EqualTo(checked((int)Math.Round(metrics.LogicalClientSize.X * 1.25f, MidpointRounding.ToEven))));
                    Assert.That(
                        metrics.ClientPixelSize.Height,
                        Is.EqualTo(checked((int)Math.Round(metrics.LogicalClientSize.Y * 1.25f, MidpointRounding.ToEven))));
                }
                finally
                {
                    if (windowCreated)
                    {
                        backend.CloseWindow(window);
                        backend.Pump();
                    }
                }
            }
        }
        finally
        {
            FreeListener(sharedMemoryListener);
            FreeListener(sharedBufferListener);
        }
    }

    [Test]
    public void RoutesInputMethodV2PreeditAndCommitThroughTextInputV3()
    {
        if (!OperatingSystem.IsLinux()
            || (Environment.GetEnvironmentVariable("TERRAFX_TEST_ISOLATED_WAYLAND_WSI") != "1")
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWAYSOCK")))
        {
            Assert.Ignore("The input-method-v2 witness requires the private Sway test environment.");
            return;
        }

        var backendOwner = PlatformBackendFactory.Create(
            PlatformBackend.WaylandOnly,
            ownerId: 1,
            ClipboardServicePolicy.NotRequested);
        WaylandTestProtocolMetadata? metadata = null;
        InputMethodClient? inputMethod = null;
        nint sharedMemoryListener = 0;
        nint sharedBufferListener = 0;

        try
        {
            using (backendOwner)
            {
                if (backendOwner is not WaylandPlatformBackend backend)
                {
                    Assert.Fail("The Wayland backend factory returned a different backend.");
                    return;
                }

                metadata = new WaylandTestProtocolMetadata(backend.Connection);
                var virtualKeyboard = new VirtualKeyboardClient(backend.Connection, metadata);
                virtualKeyboard.SetKeymap();
                inputMethod = new InputMethodClient(backend.Connection, metadata);
                inputMethod.AttachListener();
                var window = new WindowHandle(ownerId: 1, index: 0, generation: 1);
                var windowCreated = false;

                try
                {
                    backend.CreateWindow(
                        window,
                        new WindowOptions(WindowTitle) {
                            ClientSize = new Vector2(640.0f, 360.0f),
                            MinimumClientSize = new Vector2(320.0f, 180.0f),
                            Resizable = false,
                            Visible = true,
                        },
                        nativeMonitorHandle: 0);
                    windowCreated = true;

                    if (!backend.TryGetNativeWindowBinding(window, out var binding))
                    {
                        throw new InvalidOperationException("The private Wayland backend omitted its test window binding.");
                    }

                    AttachSharedBuffer(
                        backend.Connection,
                        binding.NativeWindowOrSurface,
                        ref sharedMemoryListener,
                        ref sharedBufferListener);

                    var capabilities = backend.UpdateTextInputSession(
                        window,
                        new TextInputSessionSnapshot(
                            1,
                            new TextInputSessionOptions(
                                window,
                                TextInputPurpose.FreeForm,
                                documentRevision: 1,
                                surroundingText: null,
                                selectionStart: 0,
                                selectionLength: 0,
                                caretPosition: default,
                                caretSize: default)));
                    Assert.That(capabilities.HasFlag(PlatformTextInputCapabilities.Composition), Is.True);

                    FocusSwayWindow(WindowTitle);
                    virtualKeyboard.TapLeftShift();

                    var inputSamples = new List<PlatformInputSample>();
                    var textSamples = new List<PlatformTextInputSample>();
                    PumpUntil(
                        backend,
                        inputSamples,
                        textSamples,
                        () => IsInputMethodReady(inputMethod.State, inputSamples, window),
                        "The focused text input did not activate input-method-v2.");

                    inputMethod.SetPreeditString(
                        "\u304B\u306A",
                        cursorBegin: 6,
                        cursorEnd: 6,
                        serial: inputMethod.State.DoneCount);
                    PumpUntil(
                        backend,
                        inputSamples,
                        textSamples,
                        () => TryFindTextChange(textSamples, TextInputChangeKind.CompositionUpdated, "\u304B\u306A", out _, out _),
                        "The input-method-v2 preedit did not reach the focused text-input-v3 client.");

                    Assert.That(
                        TryFindTextChange(
                            textSamples,
                            TextInputChangeKind.CompositionUpdated,
                            "\u304B\u306A",
                            out var preeditSample,
                            out var preedit),
                        Is.True);
                    Assert.That(
                        TryFindTextChange(textSamples, TextInputChangeKind.CompositionStarted, text: null, out _, out _),
                        Is.True);
                    Assert.That(preeditSample.Window, Is.EqualTo(window));
                    Assert.That(preeditSample.SessionId, Is.EqualTo(1));
                    Assert.That(preeditSample.BaseDocumentRevision, Is.EqualTo(1));
                    Assert.That(preedit.SelectionStart, Is.EqualTo(2));
                    Assert.That(preedit.SelectionLength, Is.Zero);

                    inputMethod.CommitText("\u304B\u306A", serial: inputMethod.State.DoneCount);
                    PumpUntil(
                        backend,
                        inputSamples,
                        textSamples,
                        () => TryFindTextChange(textSamples, TextInputChangeKind.TextCommitted, "\u304B\u306A", out _, out _)
                            && TryFindTextChange(textSamples, TextInputChangeKind.CompositionEnded, text: null, out _, out _),
                        "The input-method-v2 commit did not reach the focused text-input-v3 client.");

                    Assert.That(
                        TryFindTextChange(
                            textSamples,
                            TextInputChangeKind.TextCommitted,
                            "\u304B\u306A",
                            out var committedSample,
                            out _),
                        Is.True);
                    Assert.That(committedSample.Window, Is.EqualTo(window));
                    Assert.That(committedSample.SessionId, Is.EqualTo(1));
                    Assert.That(committedSample.BaseDocumentRevision, Is.EqualTo(1));
                }
                finally
                {
                    if (windowCreated)
                    {
                        _ = backend.UpdateTextInputSession(window, snapshot: null);
                        backend.Pump();
                        backend.CloseWindow(window);
                        backend.Pump();
                    }
                }
            }
        }
        finally
        {
            inputMethod?.ReleaseCallbackResources();
            FreeListener(sharedMemoryListener);
            FreeListener(sharedBufferListener);
            metadata?.Free();
        }
    }

    private static partial class XkbNative
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_context_new")]
        internal static partial nint ContextNew(uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_context_unref")]
        internal static partial void ContextUnref(nint context);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "free")]
        internal static partial void Free(nint allocation);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_get_as_string")]
        internal static partial nint KeymapGetAsString(nint keymap, int format);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_new_from_names")]
        internal static partial nint KeymapNewFromNames(nint context, XkbRuleNames* names, uint flags);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libxkbcommon.so.0", EntryPoint = "xkb_keymap_unref")]
        internal static partial void KeymapUnref(nint keymap);
    }

    private sealed unsafe class InputMethodClient
    {
        private readonly WaylandDisplayConnection _connection;
        private readonly nint _inputMethod;
        private nint* _listener;
        private readonly InputMethodState _state;
        private readonly GCHandle _stateHandle;

        internal InputMethodClient(
            WaylandDisplayConnection connection,
            WaylandTestProtocolMetadata metadata)
        {
            _connection = connection;
            var manager = connection.BindGlobal(
                connection.GetGlobals("zwp_input_method_manager_v2")[0],
                metadata.InputMethodManagerInterface,
                version: 1);
            var seat = connection.BindGlobal("wl_seat", maximumVersion: 1);
            var arguments = stackalloc WaylandArgument[2];
            arguments[0].Pointer = seat;
            arguments[1] = default;
            _inputMethod = connection.CreateProxy(
                manager,
                opcode: 0,
                metadata.InputMethodInterface,
                version: 1,
                arguments);

            if (_inputMethod == 0)
            {
                throw new IOException("The private compositor could not create an input-method-v2 client.");
            }

            _state = new InputMethodState();
            _stateHandle = GCHandle.Alloc(_state, GCHandleType.Normal);
        }

        internal InputMethodState State => _state;

        internal void AttachListener()
        {
            _listener = (nint*)NativeMemory.AllocZeroed(7, checked((nuint)sizeof(nint)));

            if (_listener is null)
            {
                throw new InvalidOperationException("The input-method-v2 listener allocation failed.");
            }

            _listener[0] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnInputMethodActivate;
            _listener[1] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnInputMethodDeactivate;
            _listener[2] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, uint, uint, void>)&OnInputMethodSurroundingText;
            _listener[3] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, void>)&OnInputMethodTextChangeCause;
            _listener[4] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, uint, uint, void>)&OnInputMethodContentType;
            _listener[5] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnInputMethodDone;
            _listener[6] = (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnInputMethodUnavailable;

            if (_connection.AddListener(_inputMethod, (nint)_listener, GCHandle.ToIntPtr(_stateHandle)) < 0)
            {
                NativeMemory.Free(_listener);
                _listener = null;
                throw new IOException("The input-method-v2 listener could not be installed.");
            }
        }

        internal void CommitText(string text, uint serial)
        {
            SendStringRequest(opcode: 1, string.Empty, cursorBegin: 0, cursorEnd: 0);
            SendStringRequest(opcode: 0, text);
            SendCommit(serial);
            _connection.FlushRequests();
        }

        internal void ReleaseCallbackResources()
        {
            if (_listener is not null)
            {
                NativeMemory.Free(_listener);
                _listener = null;
            }

            if (_stateHandle.IsAllocated)
            {
                _stateHandle.Free();
            }
        }

        internal void SetPreeditString(string text, int cursorBegin, int cursorEnd, uint serial)
        {
            SendStringRequest(opcode: 1, text, cursorBegin, cursorEnd);
            SendCommit(serial);
            _connection.FlushRequests();
        }

        private void SendCommit(uint serial)
        {
            var arguments = stackalloc WaylandArgument[1];
            arguments[0].Unsigned = serial;
            _connection.SendRequest(_inputMethod, opcode: 3, arguments);
        }

        private void SendStringRequest(uint opcode, string text)
            => SendStringRequest(opcode, text, cursorBegin: 0, cursorEnd: 0);

        private void SendStringRequest(uint opcode, string text, int cursorBegin, int cursorEnd)
        {
            var textPointer = Marshal.StringToCoTaskMemUTF8(text);

            try
            {
                var argumentCount = (opcode == 0) ? 1 : 3;
                var arguments = stackalloc WaylandArgument[argumentCount];
                arguments[0].Pointer = textPointer;

                if (argumentCount == 3)
                {
                    arguments[1].FileDescriptor = cursorBegin;
                    arguments[2].FileDescriptor = cursorEnd;
                }

                _connection.SendRequest(_inputMethod, opcode, arguments);
            }
            finally
            {
                Marshal.FreeCoTaskMem(textPointer);
            }
        }
    }

    private sealed class InputMethodState
    {
        internal uint DoneCount;

        internal bool IsActive;

        internal bool IsUnavailable;
    }

    private sealed unsafe class VirtualKeyboardClient
    {
        private readonly WaylandDisplayConnection _connection;
        private readonly nint _virtualKeyboard;

        internal VirtualKeyboardClient(
            WaylandDisplayConnection connection,
            WaylandTestProtocolMetadata metadata)
        {
            _connection = connection;
            var manager = connection.BindGlobal(
                connection.GetGlobals("zwp_virtual_keyboard_manager_v1")[0],
                metadata.VirtualKeyboardManagerInterface,
                version: 1);
            var seat = connection.BindGlobal("wl_seat", maximumVersion: 1);
            var arguments = stackalloc WaylandArgument[2];
            arguments[0].Pointer = seat;
            arguments[1] = default;
            _virtualKeyboard = connection.CreateProxy(
                manager,
                opcode: 0,
                metadata.VirtualKeyboardInterface,
                version: 1,
                arguments);

            if (_virtualKeyboard == 0)
            {
                throw new IOException("The private compositor could not create a virtual keyboard.");
            }
        }

        internal void SetKeymap()
        {
            var keymapBytes = CreateXkbKeymap();
            using var keymapFile = CreateTemporaryFile(keymapBytes);
            var arguments = stackalloc WaylandArgument[3];
            arguments[0].Unsigned = KeymapFormatXkbV1;
            arguments[1].FileDescriptor = keymapFile.SafeFileHandle.DangerousGetHandle().ToInt32();
            arguments[2].Unsigned = checked((uint)keymapBytes.Length);
            _connection.SendRequest(_virtualKeyboard, opcode: 0, arguments);
            _connection.Roundtrip();
        }

        internal void TapLeftShift()
        {
            var time = unchecked((uint)Environment.TickCount64);
            var keyArguments = stackalloc WaylandArgument[3];
            keyArguments[0].Unsigned = time;
            keyArguments[1].Unsigned = LeftShiftEvdevKey;
            keyArguments[2].Unsigned = 1;
            _connection.SendRequest(_virtualKeyboard, opcode: 1, keyArguments);
            var modifierArguments = stackalloc WaylandArgument[4];
            modifierArguments[0].Unsigned = 1;
            modifierArguments[1].Unsigned = 0;
            modifierArguments[2].Unsigned = 0;
            modifierArguments[3].Unsigned = 0;
            _connection.SendRequest(_virtualKeyboard, opcode: 2, modifierArguments);
            keyArguments[0].Unsigned = unchecked(time + 1);
            keyArguments[2].Unsigned = 0;
            _connection.SendRequest(_virtualKeyboard, opcode: 1, keyArguments);
            modifierArguments[0].Unsigned = 0;
            _connection.SendRequest(_virtualKeyboard, opcode: 2, modifierArguments);
            _connection.Roundtrip();
        }
    }

    private sealed unsafe class WaylandTestProtocolMetadata
    {
        private readonly List<nint> _allocations = [];
        private readonly List<nint> _strings = [];

        internal WaylandTestProtocolMetadata(WaylandDisplayConnection connection)
        {
            var wlSeatInterface = connection.GetInterface("wl_seat_interface");
            var wlSurfaceInterface = connection.GetInterface("wl_surface_interface");
            InputPopupSurfaceInterface = CreateInterface(
                "zwp_input_popup_surface_v2",
                version: 1,
                [new("destroy", "")],
                [new("text_input_rectangle", "iiii")]);
            InputMethodKeyboardGrabInterface = CreateInterface(
                "zwp_input_method_keyboard_grab_v2",
                version: 1,
                [new("release", "")],
                [
                    new("keymap", "uhu"),
                    new("key", "uuuu"),
                    new("modifiers", "uuuuu"),
                    new("repeat_info", "ii"),
                ]);
            InputMethodInterface = CreateInterface(
                "zwp_input_method_v2",
                version: 1,
                [
                    new("commit_string", "s"),
                    new("set_preedit_string", "sii"),
                    new("delete_surrounding_text", "uu"),
                    new("commit", "u"),
                    new("get_input_popup_surface", "no", [InputPopupSurfaceInterface, wlSurfaceInterface]),
                    new("grab_keyboard", "n", [InputMethodKeyboardGrabInterface]),
                    new("destroy", ""),
                ],
                [
                    new("activate", ""),
                    new("deactivate", ""),
                    new("surrounding_text", "suu"),
                    new("text_change_cause", "u"),
                    new("content_type", "uu"),
                    new("done", ""),
                    new("unavailable", ""),
                ]);
            InputMethodManagerInterface = CreateInterface(
                "zwp_input_method_manager_v2",
                version: 1,
                [
                    new("get_input_method", "on", [InputMethodInterface, wlSeatInterface]),
                    new("destroy", ""),
                ],
                []);
            VirtualKeyboardInterface = CreateInterface(
                "zwp_virtual_keyboard_v1",
                version: 1,
                [
                    new("keymap", "uhu"),
                    new("key", "uuu"),
                    new("modifiers", "uuuu"),
                    new("destroy", ""),
                ],
                []);
            VirtualKeyboardManagerInterface = CreateInterface(
                "zwp_virtual_keyboard_manager_v1",
                version: 1,
                [new("create_virtual_keyboard", "on", [VirtualKeyboardInterface, wlSeatInterface])],
                []);
        }

        internal nint InputMethodInterface { get; }

        internal nint InputMethodKeyboardGrabInterface { get; }

        internal nint InputMethodManagerInterface { get; }

        internal nint InputPopupSurfaceInterface { get; }

        internal nint VirtualKeyboardInterface { get; }

        internal nint VirtualKeyboardManagerInterface { get; }

        internal void Free()
        {
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
                throw new InvalidOperationException("The Wayland test protocol allocation failed.");
            }

            _allocations.Add((nint)allocation);
            return (nint)allocation;
        }

        private nint AllocateString(string value)
        {
            var stringPointer = Marshal.StringToCoTaskMemUTF8(value);
            _strings.Add(stringPointer);
            return stringPointer;
        }

        private nint CreateInterface(
            string name,
            int version,
            MessageDefinition[] requests,
            MessageDefinition[] events)
        {
            var interfaceDefinition = (NativeInterface*)AllocateArray<NativeInterface>(1);
            interfaceDefinition->Name = AllocateString(name);
            interfaceDefinition->Version = version;
            interfaceDefinition->MethodCount = requests.Length;
            interfaceDefinition->Methods = CreateMessages(requests);
            interfaceDefinition->EventCount = events.Length;
            interfaceDefinition->Events = CreateMessages(events);
            return (nint)interfaceDefinition;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct XkbRuleNames
    {
        internal nint Rules;
        internal nint Model;
        internal nint Layout;
        internal nint Variant;
        internal nint Options;
    }
}
