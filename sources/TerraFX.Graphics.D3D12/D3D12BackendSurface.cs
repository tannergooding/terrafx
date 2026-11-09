// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace TerraFX.Graphics.D3D12;

internal sealed unsafe partial class D3D12BackendSurface : IGraphicsBackendSurface
{
    private const uint DefaultMaximumFrameLatency = 1;
    private const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
    private const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);
    private const int DxgiErrorFrameStatisticsDisjoint = unchecked((int)0x887A000B);
    private const uint DxgiPresentTest = 1;
    private const int DxgiStatusOccluded = 0x087A0001;
    private const int EInvalidArgument = unchecked((int)0x80070057);
    private const int ENoInterface = unchecked((int)0x80004002);
    private const uint FrameLatencyWaitMilliseconds = 5000;
    private const uint PresentationObservationTimeoutMilliseconds = 5000;
    private const uint PresentationWaitMilliseconds = 16;
    private const uint SwapChainBufferCount = 3;
    // DXGI_USAGE_RENDER_TARGET_OUTPUT from the DXGI_SWAP_CHAIN_DESC1 contract.
    private const uint SwapChainBufferUsageRenderTargetOutput = 0x20;
    private const uint WaitFailed = uint.MaxValue;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;

    private readonly GraphicsNativeSurfaceBinding _binding;
    private SurfaceCapabilities _capabilities;
    private PixelSize _clientPixelSize;
    private readonly TextureFormat _colorFormat;
    private readonly ID3D12CommandQueue* _commandQueue;
    private readonly ID3D12Device* _device;
    private IDXGIFactory4* _factory;
    private HANDLE _frameLatencyWaitableObject;
    private readonly GraphicsCapabilities _graphicsCapabilities;
    private uint _lastSwapchainEpoch;
    private long _nextPresentationSequence;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<long, (
        uint NativePresentCount,
        uint SurfaceEpoch,
        uint SwapchainEpoch,
        PresentationStatus? TerminalStatus)> _pendingPresentations = [];
    private readonly Lock _presentationGate = new();
    private readonly List<(
        long FirstSequence,
        long LastSequence,
        uint SurfaceEpoch,
        uint SwapchainEpoch,
        PresentationStatus Status)> _presentationHistory = [];
    private readonly bool _supportsTearing;
    private readonly Func<int?>? _surfacePresentResultOverride;
    private IDXGISwapChain3* _swapChain;
    private readonly uint _swapChainFlags;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed D3D12 surface creation must release acquired native ownership before propagating the original startup failure.")]
    private D3D12BackendSurface(
        ID3D12Device* device,
        ID3D12CommandQueue* commandQueue,
        IDXGIFactory4* factory,
        IDXGISwapChain3* swapChain,
        HANDLE frameLatencyWaitableObject,
        uint swapChainFlags,
        bool supportsTearing,
        GraphicsNativeSurfaceBinding binding,
        GraphicsCapabilities graphicsCapabilities,
        SurfaceCapabilities capabilities,
        PixelSize clientPixelSize,
        TextureFormat colorFormat,
        Func<int?>? surfacePresentResultOverride)
    {
        _binding = binding;
        _device = device;
        _commandQueue = commandQueue;
        _factory = factory;
        _swapChain = swapChain;
        _frameLatencyWaitableObject = frameLatencyWaitableObject;
        _swapChainFlags = swapChainFlags;
        _supportsTearing = supportsTearing;
        _graphicsCapabilities = graphicsCapabilities;
        _capabilities = capabilities;
        _clientPixelSize = clientPixelSize;
        _colorFormat = colorFormat;
        _surfacePresentResultOverride = surfacePresentResultOverride;
        _lastSwapchainEpoch = capabilities.SwapchainEpoch;
    }

    public SurfaceCapabilities Capabilities => _capabilities;

    public PixelSize ClientPixelSize => _clientPixelSize;

    public TextureFormat ColorFormat => _colorFormat;

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed DXGI surface creation must release acquired native ownership before propagating the original startup failure.")]
    internal static D3D12BackendSurface Create(
        ID3D12Device* device,
        ID3D12CommandQueue* commandQueue,
        GraphicsNativeSurfaceBinding binding,
        PixelSize clientPixelSize,
        GraphicsCapabilities graphicsCapabilities,
        Func<int?>? surfacePresentResultOverride = null)
    {
        if (device is null)
        {
            throw new ArgumentNullException(nameof(device));
        }

        if (commandQueue is null)
        {
            throw new ArgumentNullException(nameof(commandQueue));
        }

        ArgumentNullException.ThrowIfNull(graphicsCapabilities);

        if (binding.PlatformKind != GraphicsPlatformKind.Win32)
        {
            throw new PlatformNotSupportedException("A D3D12 presentation surface requires a Win32 binding.");
        }

        var colorFormat = SelectSurfaceColorFormat(graphicsCapabilities);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clientPixelSize.Width, graphicsCapabilities.Limits.MaximumTextureDimension2D);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(clientPixelSize.Height, graphicsCapabilities.Limits.MaximumTextureDimension2D);
        var factoryId = typeof(IDXGIFactory4).GUID;
        IDXGIFactory4* factory = null;
        IDXGISwapChain3* swapChain3 = null;
        HANDLE frameLatencyWaitableObject = default;

        try
        {
            ThrowIfFailed(DirectX.CreateDXGIFactory2(0, &factoryId, (void**)&factory));

            if (factory is null)
            {
                throw new InvalidOperationException("DXGI factory creation returned no factory.");
            }

            var supportsTearing = CheckTearingSupport(factory);
            var surfaceCapabilities = CreateSurfaceCapabilities(
                graphicsCapabilities,
                colorFormat,
                binding.SurfaceEpoch,
                clientPixelSize.Width > 0 && clientPixelSize.Height > 0 ? 1u : 0u,
                supportsTearing);
            var swapChainFlags = (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT;

            if (supportsTearing)
            {
                swapChainFlags |= (uint)DXGI_SWAP_CHAIN_FLAG.DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;
            }

            if ((clientPixelSize.Width > 0) && (clientPixelSize.Height > 0))
            {
                CreateNativeSwapChain(
                    factory,
                    commandQueue,
                    binding,
                    clientPixelSize,
                    colorFormat,
                    swapChainFlags,
                    out swapChain3,
                    out frameLatencyWaitableObject);
            }

            var surface = new D3D12BackendSurface(
                device,
                commandQueue,
                factory,
                swapChain3,
                frameLatencyWaitableObject,
                swapChainFlags,
                supportsTearing,
                binding,
                graphicsCapabilities,
                surfaceCapabilities,
                clientPixelSize,
                colorFormat,
                surfacePresentResultOverride);
            factory = null;
            swapChain3 = null;
            frameLatencyWaitableObject = default;
            return surface;
        }
        catch (Exception creationException)
        {
            if ((frameLatencyWaitableObject.Value is not null) && !Windows.CloseHandle(frameLatencyWaitableObject))
            {
                var cleanupException = new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Closing the DXGI frame-latency waitable object after surface creation failed.");

                if (swapChain3 is not null)
                {
                    _ = swapChain3->Release();
                }

                throw new AggregateException(creationException, cleanupException);
            }

            if (swapChain3 is not null)
            {
                _ = swapChain3->Release();
            }

            throw;
        }
        finally
        {
            if (factory is not null)
            {
                _ = factory->Release();
            }
        }
    }

    private static bool CheckTearingSupport(IDXGIFactory4* factory)
    {
        var factoryId = typeof(IDXGIFactory5).GUID;
        IDXGIFactory5* factory5 = null;
        var queryResult = factory->QueryInterface(&factoryId, (void**)&factory5);

        if (queryResult.Value == ENoInterface)
        {
            return false;
        }

        ThrowIfFailed(queryResult);

        if (factory5 is null)
        {
            throw new InvalidOperationException("DXGI returned no factory version 5 interface.");
        }

        try
        {
            uint supportsTearing = 0;
            var result = factory5->CheckFeatureSupport(
                DXGI_FEATURE.DXGI_FEATURE_PRESENT_ALLOW_TEARING,
                &supportsTearing,
                (uint)sizeof(uint));

            if (result.Value == EInvalidArgument)
            {
                return false;
            }

            ThrowIfFailed(result);
            return supportsTearing != 0;
        }
        finally
        {
            _ = factory5->Release();
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "A failed DXGI swapchain creation must release its frame-latency handle and COM interfaces before propagating the original native failure.")]
    private static void CreateNativeSwapChain(
        IDXGIFactory4* factory,
        ID3D12CommandQueue* commandQueue,
        GraphicsNativeSurfaceBinding binding,
        PixelSize clientPixelSize,
        TextureFormat colorFormat,
        uint swapChainFlags,
        out IDXGISwapChain3* swapChain3,
        out HANDLE frameLatencyWaitableObject)
    {
        swapChain3 = null;
        frameLatencyWaitableObject = default;
        IDXGISwapChain1* swapChain1 = null;
        IDXGISwapChain3* createdSwapChain3 = null;
        HANDLE createdWaitableObject = default;
        var swapChainId = typeof(IDXGISwapChain3).GUID;
        var description = new DXGI_SWAP_CHAIN_DESC1 {
            Width = checked((uint)clientPixelSize.Width),
            Height = checked((uint)clientPixelSize.Height),
            Format = GetSwapChainFormat(colorFormat),
            Stereo = false,
            SampleDesc = new DXGI_SAMPLE_DESC {
                Count = 1,
                Quality = 0,
            },
            BufferUsage = SwapChainBufferUsageRenderTargetOutput,
            BufferCount = SwapChainBufferCount,
            Scaling = DXGI_SCALING.DXGI_SCALING_STRETCH,
            SwapEffect = DXGI_SWAP_EFFECT.DXGI_SWAP_EFFECT_FLIP_DISCARD,
            AlphaMode = DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
            Flags = swapChainFlags,
        };

        try
        {
            ThrowIfFailed(factory->CreateSwapChainForHwnd(
                (IUnknown*)commandQueue,
                new HWND((void*)binding.NativeWindowOrSurface),
                &description,
                null,
                null,
                &swapChain1));

            if (swapChain1 is null)
            {
                throw new InvalidOperationException("DXGI swapchain creation returned no swapchain.");
            }

            ThrowIfFailed(swapChain1->QueryInterface(&swapChainId, (void**)&createdSwapChain3));

            if (createdSwapChain3 is null)
            {
                throw new InvalidOperationException("DXGI did not provide the required swapchain interface.");
            }

            ThrowIfFailed(createdSwapChain3->SetMaximumFrameLatency(DefaultMaximumFrameLatency));
            createdWaitableObject = createdSwapChain3->GetFrameLatencyWaitableObject();

            if (createdWaitableObject.Value is null)
            {
                ThrowLastWin32Error("DXGI did not provide a frame-latency waitable object.");
            }

            swapChain3 = createdSwapChain3;
            createdSwapChain3 = null;
            frameLatencyWaitableObject = createdWaitableObject;
            createdWaitableObject = default;
        }
        catch (Exception creationException)
        {
            if ((createdWaitableObject.Value is not null) && !Windows.CloseHandle(createdWaitableObject))
            {
                var cleanupException = new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    "Closing the DXGI frame-latency waitable object after swapchain creation failed.");

                if (createdSwapChain3 is not null)
                {
                    _ = createdSwapChain3->Release();
                }

                throw new AggregateException(creationException, cleanupException);
            }

            if (createdSwapChain3 is not null)
            {
                _ = createdSwapChain3->Release();
            }

            throw;
        }
        finally
        {
            if (swapChain1 is not null)
            {
                _ = swapChain1->Release();
            }
        }
    }

    private static Diagnostic? CreatePresentationDiagnostic(
        PresentationStatus status,
        uint deviceEpoch,
        uint surfaceEpoch,
        uint swapchainEpoch)
    {
        if (status is PresentationStatus.Presented or PresentationStatus.Occluded or PresentationStatus.Suspended)
        {
            return null;
        }

        var code = status switch {
            PresentationStatus.DeviceLost => "graphics.presentation.device-lost",
            PresentationStatus.SurfaceLost => "graphics.presentation.surface-lost",
            PresentationStatus.SwapchainLost => "graphics.presentation.swapchain-lost",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        return new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            Stopwatch.GetTimestamp(),
            hostId: null,
            worldGuid: null,
            deviceId: null,
            surfaceId: null,
            deviceEpoch,
            surfaceEpoch,
            swapchainEpoch,
            tick: null,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation: "Surface.WaitForPresentation",
            nativeResult: null,
            message: $"D3D12 presentation ended with {status}.",
            context: []);
    }

    private static SurfaceCapabilities CreateSurfaceCapabilities(
        GraphicsCapabilities graphicsCapabilities,
        TextureFormat colorFormat,
        uint surfaceEpoch,
        uint swapchainEpoch,
        bool supportsTearing)
    {
        var maximumDimension = graphicsCapabilities.Limits.MaximumTextureDimension2D;
        ImmutableArray<GraphicsPresentMode> presentModes = supportsTearing
            ? [GraphicsPresentMode.Fifo, GraphicsPresentMode.Immediate]
            : [GraphicsPresentMode.Fifo];
        return new SurfaceCapabilities(
            [new SurfaceFormat(colorFormat, SurfaceColorSpace.SrgbNonlinear)],
            presentModes,
            minimumExtent: new PixelSize(1, 1),
            maximumExtent: new PixelSize(maximumDimension, maximumDimension),
            minimumImageCount: 2,
            maximumImageCount: 16,
            supportsTearing,
            surfaceEpoch,
            swapchainEpoch);
    }

    private static PresentationStatus? GetPresentationFailureStatus(int result)
        => result switch {
            DxgiErrorDeviceRemoved or DxgiErrorDeviceReset => PresentationStatus.DeviceLost,
            DxgiStatusOccluded => PresentationStatus.Occluded,
            _ => null,
        };

    private static DXGI_FORMAT GetSwapChainFormat(TextureFormat colorFormat)
        => colorFormat switch {
            TextureFormat.Bgra8Srgb or TextureFormat.Bgra8Unorm => DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            TextureFormat.Rgba8Srgb or TextureFormat.Rgba8Unorm => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            _ => throw new ArgumentOutOfRangeException(nameof(colorFormat)),
        };

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    private static TextureFormat SelectSurfaceColorFormat(GraphicsCapabilities capabilities)
    {
        ReadOnlySpan<TextureFormat> candidates = [
            TextureFormat.Bgra8Srgb,
            TextureFormat.Rgba8Srgb,
            TextureFormat.Bgra8Unorm,
            TextureFormat.Rgba8Unorm,
        ];

        foreach (var candidate in candidates)
        {
            foreach (var format in capabilities.Formats)
            {
                if ((format.Format == candidate)
                    && ((format.SupportedUsages & TextureUsage.ColorAttachment) != 0))
                {
                    return candidate;
                }
            }
        }

        throw new NotSupportedException("D3D12 does not support a color-attachment format for presentation.");
    }

    private static void ThrowIfFailed(HRESULT result)
    {
        if (result.FAILED)
        {
            Marshal.ThrowExceptionForHR(result.Value);
        }
    }

    private static void ThrowLastWin32Error(string message)
    {
        var error = Marshal.GetHRForLastWin32Error();

        if (error != 0)
        {
            Marshal.ThrowExceptionForHR(error);
        }

        throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        var retirementFailure = RetireSwapChain();
        var factory = _factory;
        _factory = null;

        if (factory is not null)
        {
            _ = factory->Release();
        }

        if (retirementFailure is not null)
        {
            throw retirementFailure;
        }
    }

    public SurfaceCapabilities Resize(PixelSize clientPixelSize)
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_factory is null, this);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            clientPixelSize.Width,
            _graphicsCapabilities.Limits.MaximumTextureDimension2D,
            nameof(clientPixelSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            clientPixelSize.Height,
            _graphicsCapabilities.Limits.MaximumTextureDimension2D,
            nameof(clientPixelSize));

        if ((clientPixelSize.Width == 0) || (clientPixelSize.Height == 0))
        {
            var suspendedCapabilities = CreateSurfaceCapabilities(
                _graphicsCapabilities,
                _colorFormat,
                _binding.SurfaceEpoch,
                swapchainEpoch: 0,
                _supportsTearing);
            var retirementFailure = RetireSwapChain();
            _clientPixelSize = clientPixelSize;
            _capabilities = suspendedCapabilities;

            if (retirementFailure is not null)
            {
                throw retirementFailure;
            }

            return suspendedCapabilities;
        }

        var nextSwapchainEpoch = checked(_lastSwapchainEpoch + 1);
        var resizedCapabilities = CreateSurfaceCapabilities(
            _graphicsCapabilities,
            _colorFormat,
            _binding.SurfaceEpoch,
            nextSwapchainEpoch,
            _supportsTearing);

        if (_swapChain is not null)
        {
            ThrowIfFailed(_swapChain->ResizeBuffers(
                SwapChainBufferCount,
                checked((uint)clientPixelSize.Width),
                checked((uint)clientPixelSize.Height),
                GetSwapChainFormat(_colorFormat),
                _swapChainFlags));
        }
        else
        {
            CreateNativeSwapChain(
                _factory,
                _commandQueue,
                _binding,
                clientPixelSize,
                _colorFormat,
                _swapChainFlags,
                out var swapChain,
                out var frameLatencyWaitableObject);
            _swapChain = swapChain;
            _frameLatencyWaitableObject = frameLatencyWaitableObject;
        }

        _clientPixelSize = clientPixelSize;
        _lastSwapchainEpoch = nextSwapchainEpoch;
        _capabilities = resizedCapabilities;
        return resizedCapabilities;
    }

    public bool TryGetCompletedPresentation(
        long presentationSequence,
        uint deviceEpoch,
        out PresentationReceipt? receipt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        lock (_presentationGate)
        {
            foreach (var (firstSequence, lastSequence, surfaceEpoch, swapchainEpoch, status) in _presentationHistory)
            {
                if ((presentationSequence < firstSequence)
                    || (presentationSequence > lastSequence))
                {
                    continue;
                }

                receipt = new PresentationReceipt(
                    status,
                    presentationSequence,
                    deviceEpoch,
                    surfaceEpoch,
                    swapchainEpoch,
                    CreatePresentationDiagnostic(
                        status,
                        deviceEpoch,
                        surfaceEpoch,
                        swapchainEpoch));
                return true;
            }
        }

        receipt = null;
        return false;
    }

    public PresentationReceipt WaitForPresentation(
        long presentationSequence,
        uint deviceEpoch,
        CancellationToken cancellationToken)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(presentationSequence);
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        if (TryGetCompletedPresentation(presentationSequence, deviceEpoch, out var completedReceipt))
        {
            return completedReceipt
                ?? throw new InvalidOperationException("The DXGI presentation history returned no receipt.");
        }

        if (!_pendingPresentations.TryGetValue(presentationSequence, out var presentation))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationSequence), "The sequence was not submitted by this Surface.");
        }

        if (presentation.TerminalStatus is PresentationStatus terminalStatus)
        {
            return CompletePresentation(presentationSequence, deviceEpoch, terminalStatus);
        }

        var observationTimer = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (observationTimer.ElapsedMilliseconds >= PresentationObservationTimeoutMilliseconds)
            {
                throw new TimeoutException("DXGI did not report a terminal presentation outcome before the observation timeout.");
            }

            if ((_swapChain is null)
                || (presentation.SurfaceEpoch != _capabilities.SurfaceEpoch)
                || (presentation.SwapchainEpoch != _capabilities.SwapchainEpoch))
            {
                return CompletePresentation(
                    presentationSequence,
                    deviceEpoch,
                    PresentationStatus.SwapchainLost);
            }

            if (!IsWindowVisible(_binding.NativeWindowOrSurface))
            {
                return CompletePresentation(
                    presentationSequence,
                    deviceEpoch,
                    PresentationStatus.Occluded);
            }

            var testResult = _swapChain->Present(0, DxgiPresentTest);

            if (testResult.Value == DxgiStatusOccluded)
            {
                return CompletePresentation(
                    presentationSequence,
                    deviceEpoch,
                    PresentationStatus.Occluded);
            }

            if (testResult.FAILED)
            {
                if (GetPresentationFailureStatus(testResult.Value) is PresentationStatus testFailureStatus)
                {
                    return CompletePresentation(presentationSequence, deviceEpoch, testFailureStatus);
                }

                ThrowIfFailed(testResult);
            }

            DXGI_FRAME_STATISTICS frameStatistics = default;
            var statisticsResult = _swapChain->GetFrameStatistics(&frameStatistics);

            var frameStatisticsDisjoint = statisticsResult.Value == DxgiErrorFrameStatisticsDisjoint;

            if (!frameStatisticsDisjoint && statisticsResult.FAILED)
            {
                if (GetPresentationFailureStatus(statisticsResult.Value) is PresentationStatus failedStatus)
                {
                    return CompletePresentation(presentationSequence, deviceEpoch, failedStatus);
                }

                ThrowIfFailed(statisticsResult);
            }

            if (!frameStatisticsDisjoint
                && (frameStatistics.PresentCount >= presentation.NativePresentCount))
            {
                return CompletePresentation(
                    presentationSequence,
                    deviceEpoch,
                    PresentationStatus.Presented);
            }

            var waitResult = Windows.WaitForSingleObject(
                _frameLatencyWaitableObject,
                PresentationWaitMilliseconds);

            if (waitResult == WaitFailed)
            {
                ThrowLastWin32Error("Waiting for D3D12 presentation progress failed.");
            }

            if (waitResult == WaitObject0)
            {
                return CompletePresentationAfterLatencySignal(presentationSequence, deviceEpoch);
            }
        }
    }

    public ImmutableArray<PresentationReceipt> WaitForPresentations(uint deviceEpoch)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        if (_pendingPresentations.Count == 0)
        {
            return [];
        }

        return WaitForFrameLatency(deviceEpoch);
    }

    internal long Present()
    {
        EnsureOwnerThread();
        var swapChain = _swapChain;

        if (swapChain is null)
        {
            throw new InvalidOperationException("The D3D12 Surface has no active swapchain.");
        }

        if (_pendingPresentations.Count != 0)
        {
            throw new InvalidOperationException("The previous D3D12 presentation has not reached a terminal state.");
        }

        var presentationSequence = checked(_nextPresentationSequence + 1);
        _nextPresentationSequence = presentationSequence;
        int presentResult;

        if (_surfacePresentResultOverride?.Invoke() is int overrideResult)
        {
            if (overrideResult is 0 or (> 0 and not DxgiStatusOccluded))
            {
                throw new InvalidOperationException("The D3D12 Surface present-result override must report a failure or occlusion.");
            }

            presentResult = overrideResult;
        }
        else
        {
            presentResult = swapChain->Present(1, 0).Value;
        }

        if (presentResult == DxgiStatusOccluded)
        {
            _pendingPresentations.Add(presentationSequence, (
                0,
                _capabilities.SurfaceEpoch,
                _capabilities.SwapchainEpoch,
                PresentationStatus.Occluded));
            return presentationSequence;
        }

        if (presentResult < 0)
        {
            if (GetPresentationFailureStatus(presentResult) is PresentationStatus status)
            {
                _pendingPresentations.Add(presentationSequence, (
                    0,
                    _capabilities.SurfaceEpoch,
                    _capabilities.SwapchainEpoch,
                    status));
                return presentationSequence;
            }

            Marshal.ThrowExceptionForHR(presentResult);
        }

        uint nativePresentCount = 0;
        ThrowIfFailed(swapChain->GetLastPresentCount(&nativePresentCount));

        if (nativePresentCount == 0)
        {
            throw new InvalidOperationException("DXGI returned an empty presentation count after Present succeeded.");
        }

        _pendingPresentations.Add(presentationSequence, (
            nativePresentCount,
            _capabilities.SurfaceEpoch,
            _capabilities.SwapchainEpoch,
            null));
        return presentationSequence;
    }

    internal void RecordPresentationCopy(ID3D12GraphicsCommandList* commandList, ID3D12Resource* source)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(commandList);
        ArgumentNullException.ThrowIfNull(source);
        var swapChain = _swapChain;

        if (swapChain is null)
        {
            throw new InvalidOperationException("The D3D12 Surface has no active swapchain.");
        }

        ID3D12Resource* backBuffer = null;
        var resourceId = typeof(ID3D12Resource).GUID;

        try
        {
            ThrowIfFailed(swapChain->GetBuffer(
                swapChain->GetCurrentBackBufferIndex(),
                &resourceId,
                (void**)&backBuffer));

            if (backBuffer is null)
            {
                throw new InvalidOperationException("DXGI returned no current swapchain backbuffer.");
            }

            var toCopyDestination = D3D12_RESOURCE_BARRIER.InitTransition(
                backBuffer,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                uint.MaxValue,
                D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
            commandList->ResourceBarrier(1, &toCopyDestination);
            commandList->CopyResource(backBuffer, source);
            var toPresent = D3D12_RESOURCE_BARRIER.InitTransition(
                backBuffer,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_COPY_DEST,
                D3D12_RESOURCE_STATES.D3D12_RESOURCE_STATE_PRESENT,
                uint.MaxValue,
                D3D12_RESOURCE_BARRIER_FLAGS.D3D12_RESOURCE_BARRIER_FLAG_NONE);
            commandList->ResourceBarrier(1, &toPresent);
        }
        finally
        {
            if (backBuffer is not null)
            {
                _ = backBuffer->Release();
            }
        }
    }

    internal ImmutableArray<PresentationReceipt> WaitForFrameLatency(uint deviceEpoch)
    {
        EnsureOwnerThread();
        ArgumentOutOfRangeException.ThrowIfZero(deviceEpoch);

        if (_frameLatencyWaitableObject.Value is null)
        {
            throw new InvalidOperationException("The D3D12 Surface has no active frame-latency waitable object.");
        }

        var waitResult = Windows.WaitForSingleObject(_frameLatencyWaitableObject, FrameLatencyWaitMilliseconds);

        if (waitResult == WaitFailed)
        {
            ThrowLastWin32Error("Waiting for the D3D12 Surface frame-latency object failed.");
        }

        if (waitResult == WaitTimeout)
        {
            throw new TimeoutException("The D3D12 Surface frame-latency wait timed out.");
        }

        if (waitResult != WaitObject0)
        {
            throw new InvalidOperationException("The D3D12 Surface frame-latency wait returned an unknown status.");
        }

        return CompletePendingPresentationsAfterLatencySignal(deviceEpoch);
    }

    private ImmutableArray<PresentationReceipt> CompletePendingPresentationsAfterLatencySignal(uint deviceEpoch)
    {
        var sequences = new long[_pendingPresentations.Count];
        _pendingPresentations.Keys.CopyTo(sequences, 0);
        Array.Sort(sequences);
        var receipts = ImmutableArray.CreateBuilder<PresentationReceipt>(sequences.Length);

        foreach (var sequence in sequences)
        {
            var status = GetPresentationStatusAfterLatencySignal(sequence);
            receipts.Add(CompletePresentation(sequence, deviceEpoch, status));
        }

        return receipts.MoveToImmutable();
    }

    private PresentationReceipt CompletePresentation(
        long presentationSequence,
        uint deviceEpoch,
        PresentationStatus status)
    {
        if (!_pendingPresentations.TryGetValue(presentationSequence, out var presentation))
        {
            if (TryGetCompletedPresentation(presentationSequence, deviceEpoch, out var completedReceipt))
            {
                return completedReceipt
                    ?? throw new InvalidOperationException("The DXGI presentation history returned no receipt.");
            }

            throw new ArgumentOutOfRangeException(nameof(presentationSequence));
        }

        var receipt = new PresentationReceipt(
            status,
            presentationSequence,
            deviceEpoch,
            presentation.SurfaceEpoch,
            presentation.SwapchainEpoch,
            CreatePresentationDiagnostic(
                status,
                deviceEpoch,
                presentation.SurfaceEpoch,
                presentation.SwapchainEpoch));
        _ = _pendingPresentations.Remove(presentationSequence);

        lock (_presentationGate)
        {
            if (_presentationHistory.Count != 0)
            {
                var (previousFirstSequence, previousLastSequence, previousSurfaceEpoch, previousSwapchainEpoch, previousStatus)
                    = _presentationHistory[^1];

                if ((previousLastSequence < long.MaxValue)
                    && ((previousLastSequence + 1) == presentationSequence)
                    && (previousSurfaceEpoch == presentation.SurfaceEpoch)
                    && (previousSwapchainEpoch == presentation.SwapchainEpoch)
                    && (previousStatus == status))
                {
                    _presentationHistory[^1] = (
                        previousFirstSequence,
                        presentationSequence,
                        previousSurfaceEpoch,
                        previousSwapchainEpoch,
                        previousStatus);
                    return receipt;
                }
            }

            _presentationHistory.Add((
                presentationSequence,
                presentationSequence,
                presentation.SurfaceEpoch,
                presentation.SwapchainEpoch,
                status));
        }

        return receipt;
    }

    private PresentationReceipt CompletePresentationAfterLatencySignal(
        long presentationSequence,
        uint deviceEpoch)
        => CompletePresentation(
            presentationSequence,
            deviceEpoch,
            GetPresentationStatusAfterLatencySignal(presentationSequence));

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("The D3D12 surface is restricted to its creating graphics owner thread.");
        }
    }

    private PresentationStatus GetPresentationStatusAfterLatencySignal(long presentationSequence)
    {
        if (!_pendingPresentations.TryGetValue(presentationSequence, out var presentation))
        {
            throw new ArgumentOutOfRangeException(nameof(presentationSequence));
        }

        if (presentation.TerminalStatus is PresentationStatus terminalStatus)
        {
            return terminalStatus;
        }

        if (!IsWindowVisible(_binding.NativeWindowOrSurface))
        {
            return PresentationStatus.Occluded;
        }

        var swapChain = _swapChain;

        if (swapChain is null)
        {
            throw new InvalidOperationException("The D3D12 Surface lost its swapchain before presentation completion.");
        }

        var testResult = swapChain->Present(0, DxgiPresentTest);

        if (testResult.Value == DxgiStatusOccluded)
        {
            return PresentationStatus.Occluded;
        }

        if (testResult.FAILED)
        {
            if (GetPresentationFailureStatus(testResult.Value) is PresentationStatus status)
            {
                return status;
            }

            ThrowIfFailed(testResult);
        }

        return PresentationStatus.Presented;
    }

    private Win32Exception? RetireSwapChain()
    {
        var frameLatencyWaitableObject = _frameLatencyWaitableObject;
        _frameLatencyWaitableObject = default;
        var swapChain = _swapChain;
        _swapChain = null;
        var closeFailure = (frameLatencyWaitableObject.Value is not null) && !Windows.CloseHandle(frameLatencyWaitableObject)
            ? new Win32Exception(Marshal.GetLastPInvokeError(), "Closing the DXGI frame-latency waitable object failed.")
            : null;

        if (swapChain is not null)
        {
            _ = swapChain->Release();
        }

        return closeFailure;
    }
}
