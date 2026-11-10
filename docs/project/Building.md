# Building the repository

The repository aims to be simple to configure, build, and produce binaries. To achieve this it pulls in all dependencies as NuGet packages and provides a set of build scripts to assist in performing common tasks such as restoring dependencies, building, testing, and produce NuGet packages.

These scripts are:
 * [scripts/build.ps1](../../scripts/build.ps1) on Windows
 * [scripts/build.sh](../../scripts/build.sh) on Linux

Each script performs the requested build steps and expects that the .NET SDK is already on the box. This means that after a clean enlistment, you must select both the `restore` and `build` steps (in that order).

In addition there are [scripts/cibuild.cmd](../../scripts/cibuild.cmd) and [scripts/cibuild.sh](../../scripts/cibuild.sh) scripts which allow you to reproduce everything the CI environment does. This includes automatically acquiring a compatible .NET SDK and running each step in the correct sequence (first restore, then build, then test, then pack). Run Debug before Release.

Specifying `-architecture` or `--architecture` also acquires the published SDK version from [global.json](../../global.json) under `artifacts\dotnet`. CI's SDK setup uses the same file; update its version for RC/GA releases and servicing without changing the scripts or workflow. Installed SDK selection retains the configured roll-forward policy.

## Requirements

.NET 11.0 SDK: https://dotnet.microsoft.com/download/dotnet/11.0

## Available Arguments

The scripts accept any combination of build steps and additionally forward any user specified arguments given.

For example, `scripts/build.ps1 -build -restore` runs both steps. The scripts are aware of command ordering requirements and will run them in the correct order (e.g. `restore` will always run before `build`).

The Windows arguments take a single dash (`-`) as the prefix; Bash arguments use two (`--`).

The available arguments can be seen via the `help` command, but are additionally listed below for reference:
```
Common settings:
  -configuration <value>  Build configuration (Debug, Release)
  -verbosity <value>      MSBuild verbosity (quiet, minimal, normal, detailed, diagnostic)
  -help                   Print help and exit

Actions:
  -restore                Restore dependencies
  -build                  Build solution
  -test                   Run all tests in the solution
  -pack                   Package build artifacts

Advanced settings:
  -solution <value>       Path to solution or project to build
  -ci                     Acquire the SDK and run restore, build, test, pack
  -architecture <value>   SDK architecture (<auto>, amd64, x64, x86, arm64, arm)
  -additionalArgs <array> Forward extra options (PowerShell)

Command line arguments not listed above are passed through to dotnet.
PowerShell arguments can be shortened as much as to be unambiguous (e.g. -co for configuration, -t for test, etc.).
```

Use `-additionalArgs @("...")` for options that conflict with PowerShell parameters. Bash accepts `--` before forwarded arguments.

## Utilizing `dotnet`

Given that the above build scripts all utilize `dotnet` behind the scenes, you can also utilize the same for working with the repository.

That is, all of the existing well known commands "just work". This includes, but is not limited to:
 * `dotnet restore`
 * `dotnet build`
 * `dotnet test`
 * `dotnet pack`

With a compatible SDK installed, `dotnet build` also restores dependencies automatically; no script or SDK acquisition step is required.

Test filtering uses the runner's existing `FullyQualifiedName` support, as in the foundation-codec commands below. For implemented cases, confirm nonzero selected and executed counts and inspect current-run TRX results under `artifacts\tst`. A successful command with no selected tests is not validation.

Run recipient-local input recording/replay regressions on Windows:

```powershell
dotnet test .\tests\TerraFX.Input.Tests\TerraFX.Input.Tests.csproj --configuration Debug --filter "FullyQualifiedName~InputRecordingTests"
dotnet test .\tests\TerraFX.Input.Tests\TerraFX.Input.Tests.csproj --configuration Release --filter "FullyQualifiedName~InputRecordingTests"
```

In the configured WSLg shell below, run the same selection with Linux outputs isolated under `artifacts/linux`:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Input.Tests/TerraFX.Input.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~InputRecordingTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Input.Tests/TerraFX.Input.Tests.csproj" --configuration Release --filter "FullyQualifiedName~InputRecordingTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

### Graphics capability contracts

Run the owning-project tests for immutable device/surface capabilities, profile admission, buffer/texture/sampler descriptions, and invalid boundary rejection:

```powershell
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Release
```

The fake-backend transient-texture group test verifies shared allocation ownership through retirement and active-use completion; it does not qualify Vulkan or D3D12 native alias allocation:

```powershell
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug --filter "FullyQualifiedName~TransientTextureGroupRetainsSharedAllocationUntilActiveUseCompletes"
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Release --filter "FullyQualifiedName~TransientTextureGroupRetainsSharedAllocationUntilActiveUseCompletes"
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug --filter "FullyQualifiedName~TransientTextureGroupRollbackReleasesSharedAllocationAndBudget"
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Release --filter "FullyQualifiedName~TransientTextureGroupRollbackReleasesSharedAllocationAndBudget"
```

Run the D3D12 descriptor-range allocation and graph resource-state mapping tests (43/43 in Debug and Release):

```powershell
dotnet test .\tests\TerraFX.Graphics.D3D12.Tests\TerraFX.Graphics.D3D12.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.D3D12.Tests\TerraFX.Graphics.D3D12.Tests.csproj --configuration Release
```

Build the D3D12 package to compile its adapter enumeration and capability reporting path. This does not initialize a device or qualify native startup:

```powershell
dotnet build .\sources\TerraFX.Graphics.D3D12\TerraFX.Graphics.D3D12.csproj --configuration Debug
dotnet build .\sources\TerraFX.Graphics.D3D12\TerraFX.Graphics.D3D12.csproj --configuration Release
```

### Surface output format compatibility

`Surface.ColorFormat` is the selected native format; `Surface.PresentationSourceFormat` is its same-channel sRGB graph-source format. The compatibility tests verify sRGB and UNorm mapping, Vulkan's sRGB-first selection and capability-checked UNorm fallback, and native D3D12 presentation:

```powershell
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SurfaceFormatCompatibilityTests"
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Release --filter "FullyQualifiedName~SurfaceFormatCompatibilityTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~VulkanSurfaceFormatSelectorTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~VulkanSurfaceFormatSelectorTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~D3D12BackendCreatesAndCompilesWin32FlipDiscardSurface|FullyQualifiedName~EngineHostInteractiveStartupMapsSurfaceBeforeWindowReceipt"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~D3D12BackendCreatesAndCompilesWin32FlipDiscardSurface|FullyQualifiedName~EngineHostInteractiveStartupMapsSurfaceBeforeWindowReceipt"
```

The unknown-present quarantine case runs in a child testhost because safe shutdown intentionally retains process-global native ownership until process exit.

### EngineHost graphics recovery

These fake-backend tests cover retry after a failed OffscreenNative replacement, device-epoch advancement, and Interactive Surface-epoch/metrics republishing. Interactive coverage requires Windows; the OffscreenNative case also runs on WSLg and does not qualify native device-loss recovery:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~RecoversInteractiveGraphicsAndPublishesNewSurfaceEpoch|FullyQualifiedName~RecoversSuspendedOffscreenGraphicsWithNextDeviceEpoch"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~RecoversInteractiveGraphicsAndPublishesNewSurfaceEpoch|FullyQualifiedName~RecoversSuspendedOffscreenGraphicsWithNextDeviceEpoch"
```

### D3D12 OffscreenNative startup and descriptor-set probes

The focused Runtime tests exercise D3D12 startup and descriptor writes using a `PlatformHostOwner`-issued identity:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll compile-m04-shader .\artifacts\shader-fixtures\m04-offscreen-triangle-d3d12-witness
$env:TERRAFX_M04_SHADER_FIXTURE_DIRECTORY = (Resolve-Path .\artifacts\shader-fixtures\m04-offscreen-triangle-d3d12-witness).Path
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~CreatesAndShutsDownD3D12GraphicsDeviceUsingHostIssuedIdentity"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~CreatesAndShutsDownD3D12GraphicsDeviceUsingHostIssuedIdentity"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.CreatesD3D12DescriptorSetsAndRetainsNativeOwners"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.CreatesD3D12DescriptorSetsAndRetainsNativeOwners"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.D3D12BufferTransferRetainsOwnersUntilFenceCompletionIsObserved"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.D3D12BufferTransferRetainsOwnersUntilFenceCompletionIsObserved"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.D3D12TimeoutAfterNativeFenceSignalQuarantinesResource"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.D3D12TimeoutAfterNativeFenceSignalQuarantinesResource"
```

On the current Windows 11 x64 host, the native tests pass on the AMD Radeon RX 7900 XTX (vendor `1002`, device `744C`, UMD `002000007D0F1B75`). D3D12 admits sampled D32 depth through an `R32_TYPELESS` resource, with `D32_FLOAT` depth and `R32_FLOAT` sampled view formats; the startup witness creates a real depth/sampled texture and verifies an exact upload/readback round trip. It also round-trips a 4x4x2 RGBA8 volume and creates a resource-bearing PSO/root signature from compiled DXIL. The startup witness uploads and reads back exactly 64 MiB through the staging rings, then uploads and reads back 64 MiB + 1 through dedicated staging, verifying bytes at the end of each range. The new native transfer witness uses an internal waiter hook that calls `WaitForSingleObject` on the real D3D12 fence and delays reporting its signaled result. While that result is held, the caller disposes the source lease/owner and destination and requests shutdown; the destination-use count stays active and shutdown remains pending. Releasing the result lets the upload complete with a fence and shutdown retire resources. A second case reports an injected `WAIT_TIMEOUT` after the real fence has signaled; the upload faults, the resource refuses retirement while transfer completion is marked uncertain, and GraphicsDevice quarantines the backend. This validates conservative timeout handling with an injected result, not a driver-produced timeout or device-loss observation. The full fixture-selected Runtime.Tests suite passes 511/512 (one 96-DPI hardware skip) in Debug/Release; TRX: `artifacts\\tst\\Debug\\tagoo_GOODING-ZEN4_2026-11-02_18_08_47_net11.0.trx` / `artifacts\\tst\\Release\\tagoo_GOODING-ZEN4_2026-11-02_18_09_14_net11.0.trx`. The descriptor-set test directly creates the D3D12 backend on its test thread using the Core owner ID issued by `PlatformHostOwner`, then writes the `PSSampledFrameStorageMain` Frame CBV/SRV/UAV/sampler tables for a uniform buffer, sampled texture, structured storage buffer, storage image, and sampler. Both native PSO paths now consume typed reflection generated from the same fixture's DXIL dumpbin and SPIRV-Cross outputs. This validates selected fixture bindings and resource shapes, not generic record-layout association or complete bytecode/reflection equivalence. The descriptor test verifies incomplete, misaligned, and invalid-usage/range refusal plus pipeline/resource/backend retention until set disposal. Storage buffers require a declared structured stride; storage textures are single-layer 2D. Neither test binds tables to a command list, executes the shader, or submits a graph, so neither is the public graph/pipeline witness.

The descriptor witness holds 128 live sampler-backed sets to fill the 256-slot sampler heap, verifies that the next sampler admission is refused, then retires one set/sampler and confirms both ranges can be reused by a replacement set. This verifies native descriptor-capacity refusal and recovery, not GPU-completion retirement.

The same native witness intentionally changes the typed sampled-image register from `t0` to `t1` while keeping the compiled DXIL unchanged; D3D12 rejects the mismatched pipeline state. This validates resource-register compatibility for the selected fixture, not complete reflection equivalence.

### Platform owner contracts

Run the owning-project tests for explicit platform selection, the Headless owner/capability path, Windows PMv2/STA startup, and foreground Raw Input registration/admission:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release
```

The Windows TSF selection exercises protected-session input scopes/redaction, synchronous and asynchronous text-store locks, UTF-16 range validation, composition commit/cancel, and native document-context activation/update/retirement. It requires Windows 11 x64 and does not claim physical IME preedit/commit:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~WindowsTextInputStoreTests|FullyQualifiedName~WindowsTextInputTsfTests|FullyQualifiedName~WindowsTextInputSessionStateTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~WindowsTextInputStoreTests|FullyQualifiedName~WindowsTextInputTsfTests|FullyQualifiedName~WindowsTextInputSessionStateTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~PlatformInputRoutingOwnerTests.ClosesTextSessionBeforeRetiringItsNativeWindowState|FullyQualifiedName~PlatformInputRoutingOwnerTests.RoutesTextSessionUnavailableOnNativeTsfSetupFailure|FullyQualifiedName~PlatformInputRoutingOwnerTests.RoutesCommittedPlatformTextToActiveHostLiveUiSession|FullyQualifiedName~PlatformInputRoutingOwnerTests.RoutesTextSessionCommitUnavailableWithoutNativeSource|FullyQualifiedName~PlatformInputRoutingOwnerTests.RoutesTextSessionCompositionUnavailableOnCommittedComposeFallback"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~PlatformInputRoutingOwnerTests.ClosesTextSessionBeforeRetiringItsNativeWindowState|FullyQualifiedName~PlatformInputRoutingOwnerTests.RoutesTextSessionUnavailableOnNativeTsfSetupFailure|FullyQualifiedName~PlatformInputRoutingOwnerTests.RoutesCommittedPlatformTextToActiveHostLiveUiSession|FullyQualifiedName~PlatformInputRoutingOwnerTests.RoutesTextSessionCommitUnavailableWithoutNativeSource|FullyQualifiedName~PlatformInputRoutingOwnerTests.RoutesTextSessionCompositionUnavailableOnCommittedComposeFallback"
```

The WSLg selection can execute the portable state and Runtime-routing tests; TSF context/store and Windows clipboard station witnesses are Windows-only and are not executed on Linux.

The Windows `CF_UNICODETEXT` witness runs a child test process attached at process creation to an unnamed, noninteractive window station and test-owned desktop. The child verifies both native object names and rejects `WinSta0` before opening the clipboard, then writes and reads a bounded Unicode payload; it never accesses the interactive user's clipboard. The child-only test is intentionally skipped when run directly; invoke the parent witness below:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Platform.Tests.WindowsClipboardIsolationTests.ReadsAndWritesClipboardOnATestOwnedWindowStation"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName=TerraFX.Platform.Tests.WindowsClipboardIsolationTests.ReadsAndWritesClipboardOnATestOwnedWindowStation"
```

In the WSLg shell configured below, the same project runs the explicit X11 owner witnesses when `DISPLAY` is available. They verify an invisible native window's pixel metrics/private binding, validate reported `Xft.dpi` conversion or the documented assumed-96-DPI fallback, send duplicate synthetic `WM_DELETE_WINDOW` messages to check close-request deduplication, exercise RandR 1.5 monitor enumeration/selection when the X server exposes it, and inject synthetic core key/button/motion events through the owner pump, including the legacy release/press auto-repeat pair. The input witness also requests a fresh keyboard/pointer reconciliation snapshot. The Wayland window witness requests a window-scoped reconciliation snapshot from the focus-scoped keyboard/pointer state mirror; it does not query global device state. When XInput2 2.0 and `libXi` are available, the X11 backend selects master-device events and preserves source IDs; Xlib core-event fallback uses aggregate broker identities. `libxkbcommon-x11` key names map physical HID controls; if that library is absent, the limited fallback uses the WSLg evdev `+8` keycode convention. The WSLg lane privately extracts `libxkbcommon-x11` and `libxcb-xkb` under `artifacts/linux/native/extracted` without changing system packages. Its X11 witness reports `XInput2=True` and `XKB keymap=True`; synthetic keycode 38 resolves through the live keymap to HID usage `AC01` and emits a separate bounded text-change sample after the physical key sample in the owner capture order. This exercises the XKB compose-to-text path with `XSendEvent`, not XInput2 event delivery, physical typing, or live keymap-change notification. The current WSLg lane has no `Xft.dpi` resource, so it observes the assumed-96-DPI fallback; the synthetic close message does not qualify behavior from a real window manager:

The X11 owner witness also requests a 640x360 logical client size, verifies current-scale pixel rounding and refreshed metrics, and applies a native window title. Its mode witness requests EWMH fullscreen and waits for the observed `_NET_WM_STATE`; when confirmation is still pending, it verifies a second request returns `QueueFull` with sequence zero and no replacement. WSLg confirms `Applied` with `BorderlessFullscreen` observed in Debug and Release. This does not qualify public EngineHost window creation or exclusive fullscreen; Vulkan WSI has separate Win32, Wayland, and Xlib Surface witnesses. WSLg Vulkan witnesses inject acquire `OUT_OF_DATE`/`SURFACE_LOST`/`TIMEOUT`/`NOT_READY` before native acquire, verifying recreation with stale-graph rejection, loss retirement, and no-image retry. Separate present-result witnesses inject `OUT_OF_DATE`/`SURFACE_LOST` after native enqueue and verify fence-delayed receipts carrying the native VkResult; an injected unknown result conservatively quarantines the Surface and GraphicsDevice. A present-fence wait override forces timeout after the real fence has signaled and verifies Surface/device quarantine. These Debug/Release tests cover backend result handling and conservative quarantine, not driver-generated failure or timeout.

The isolated Xvfb/XTest reconciliation witness requires Xvfb with its XTEST server extension and the `libXtst` client library. It drives focus through XSetInputFocus, presses a key and pointer button through XTEST, and confirms window-scoped snapshots reflect server-held state before and after focus transfer/release. This exercises X server delivery and `XQueryKeymap`/`XQueryPointer`, not physical devices or window-manager focus policy. With test-owned Xvfb listening on `:99`, run:

```bash
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName=TerraFX.Platform.Tests.HeadlessPlatformBackendTests.X11OnlyReconcilesLiveKeyAndPointerStateAcrossFocusChangesOnIsolatedDisplay" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName=TerraFX.Platform.Tests.HeadlessPlatformBackendTests.X11OnlyReconcilesLiveKeyAndPointerStateAcrossFocusChangesOnIsolatedDisplay" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

The X11 clipboard witnesses require the test-owned Xvfb `:99`; they never run against WSLg's shared `:0` display or touch the user's clipboard. They cover empty selection, a 500KB `UTF8_STRING` round trip (using `INCR`), size refusal, selection-owner change, no-selection after owner close, stalled-INCR timeout, the advertised `TARGETS` list, malformed text format, and malformed `INCR` header. Pure UTF-8 decoder tests cover malformed and over-limit bytes. In WSLg, the private Xvfb and extracted `xkbcomp` are run inside bubblewrap, binding `xkbcomp` at the server's `/usr/bin/xkbcomp` path; the clipboard witnesses pass 7/7 in Debug and Release. This is isolated `:99` evidence, not access to the user's `:0` clipboard:

The Xvfb binary's `xkbcomp` helper is hard-coded at `/usr/bin/xkbcomp`. In WSLg, keep Xvfb and the test client in one bubblewrap namespace with a private `/tmp`, and bind the extracted helper to that path; do not bind or copy WSLg's `:0` clipboard state.

```bash
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~TerraFX.Platform.Tests.X11ClipboardTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~TerraFX.Platform.Tests.X11ClipboardTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

The Runtime recipient-fanout witness uses the same isolated Xvfb/XTest setup to pass a captured key through `PlatformInputBroker` and `PlatformInputRoutingOwner` to both `HostLiveUI` and the bound World timeline. It also opens a HostLiveUI text session and verifies the native XKB keymap/compose commit reaches it. The witness holds a key across focus loss/regain and verifies the focus-scoped `StateReconciliation` reports it held and reaches both recipients. `HostLiveUI` remains disarmed during that snapshot; after the key is released and pressed again, both recipients publish the fresh press. This is X-server/XTest evidence, not physical hardware or a UI toolkit capture callback:

```bash
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.PlatformInputRoutingOwnerTests.RoutesNativeX11CaptureToHostLiveUiAndBoundGameplayTimeline" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.PlatformInputRoutingOwnerTests.RoutesNativeX11CaptureToHostLiveUiAndBoundGameplayTimeline" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

On Windows, the D3D12 Interactive EngineHost witness submits and observes two native Surface presentations. Separate pre-`Present` result injections verify `Occluded` and `DeviceLost` terminal receipt/diagnostic mapping, followed by Surface cleanup; neither simulates DXGI reporting those results. A hidden-window run also observes `Occluded` receipts through native window visibility, not a driver-produced status. An unrecognized `Present(TEST)` HRESULT injection during receipt observation verifies Surface/device quarantine, native-result propagation to `graphics-device-lost`, and shutdown failure with the native HRESULT retained; it exercises conservative failure policy, not a driver-reported error. The DeviceLost case verifies the preserved native HRESULT, `StopHost`, and `SuspendPresentation`, including continued hosted World ticks with later graphics submissions rejected.

The live `Xft.dpi` transition witness changes the X server's root `RESOURCE_MANAGER` property, so run it only against a test-owned Xvfb display with `xkbcomp` and XKB data available. Set `TERRAFX_TEST_ISOLATED_X11=1` with that isolated `DISPLAY`; never set the flag against WSLg's shared `:0` display. The witness verifies the reported 96-to-144 DPI transition, content-scale refresh, and a 640x360 logical client-size request producing 960x540 pixels. With Xvfb listening on `:99`, run:

```bash
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName=TerraFX.Platform.Tests.HeadlessPlatformBackendTests.X11OnlyRefreshesDpiAfterResourceManagerChangesOnAnIsolatedDisplay" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName=TerraFX.Platform.Tests.HeadlessPlatformBackendTests.X11OnlyRefreshesDpiAfterResourceManagerChangesOnAnIsolatedDisplay" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

After setting `WORKTREE` as shown in the WSLg command block below, retrieve and extract the optional XKB client libraries privately; do not install system packages:

```bash
apt-get download libxkbcommon-x11-0 libxcb-xkb1
for package in libxkbcommon-x11-0_*.deb libxcb-xkb1_*.deb; do
  dpkg-deb -x "$package" "$WORKTREE/artifacts/linux/native/extracted"
done
export LD_LIBRARY_PATH="$WORKTREE/artifacts/linux/native/extracted/usr/lib/x86_64-linux-gnu${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
```

The X11 owner witness also verifies that a synthetic printable key produces committed text from the live XKB keymap/state and compose path. This exercises XSendEvent normalization, not physical typing or XInput2 delivery:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName=TerraFX.Platform.Tests.HeadlessPlatformBackendTests.X11OnlyCreatesAndRetiresAnInvisibleNativeWindowWhenDisplayIsAvailable" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName=TerraFX.Platform.Tests.HeadlessPlatformBackendTests.X11OnlyCreatesAndRetiresAnInvisibleNativeWindowWhenDisplayIsAvailable" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.X11PlatformOwnerReportsEwmhFullscreenConfirmationOrRefusal" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.X11PlatformOwnerReportsEwmhFullscreenConfirmationOrRefusal" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

The standard Platform.Tests selection also exercises `WaylandDisplayConnectionTests` when `WAYLAND_DISPLAY` is available. On WSLg, the Wayland adapter binds compositor/output/seat globals, initializes the compositor keyboard keymap, modifier state, compose table, and repeat schedule, creates an xdg-shell toplevel, acknowledges its configure, applies minimum/resizability size hints and a title, reports logical/pixel metrics and a Wayland surface binding, and retires the window. The compositor-window selection passes 3/3, and the hardware-independent repeat-schedule tests pass 5/5 in Debug and Release. This does not observe physical keyboard/pointer events, text-input-v3, clipboard, a mapped/presented buffer, or WSI.

`WaylandClipboardTransferTests` uses in-memory streams to verify bounded UTF-8 decoding, malformed/oversized payloads, caller cancellation, timeout, and early source failure. It does not open a Wayland clipboard or touch WSLg's shared selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~WaylandClipboardTransferTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~WaylandClipboardTransferTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

An isolated nested-X11 Weston compositor on test-owned Xvfb exposes the Wayland seat/data-device and completes a `wl-copy`/`wl-paste` UTF-8 round trip. This is compositor-client protocol evidence only; it does not qualify `ClipboardTextService` gesture authorization or refusal/cancellation/timeout outcomes, and it does not use WSLg's shared `:0` selection.

The private Weston/Xvfb runtime must use bubblewrap with a private `/tmp`, because WSLg mounts `/tmp/.X11-unix` read-only. The no-clipboard PlatformOwner startup witness passes, but an unmapped Wayland window receives no XTest pointer event. The separate EngineHost/Vulkan integration witness aborts in `libwayland-client`/libffi event dispatch around WSI before clipboard operations; no service gesture/result is claimed. These tests never use WSLg `:0`, its clipboard, or `/dev/uinput`.

Wayland fallback tests require an available X11 display and an intentionally unavailable Wayland socket. Run them with the WSLg environment configured above:

```bash
TERRAFX_TEST_WAYLAND_UNAVAILABLE=1 WAYLAND_DISPLAY=terrafx-unavailable "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~WaylandThenX11SelectsX11AndRetainsWaylandFailureWhenDisplayIsAvailable|FullyQualifiedName~WaylandFallbackDiagnosticIsDrainedOnce" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
TERRAFX_TEST_WAYLAND_UNAVAILABLE=1 WAYLAND_DISPLAY=terrafx-unavailable "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~WaylandThenX11SelectsX11AndRetainsWaylandFailureWhenDisplayIsAvailable|FullyQualifiedName~WaylandFallbackDiagnosticIsDrainedOnce" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

Wayland input normalization is implemented for keyboard keycodes/keymap names, pointer buttons, absolute motion, continuous scroll, and libxkbcommon compose commits. Native text changes use a bounded queue separate from physical samples but share their capture order; committed compose fallback records are grouped per focused window at drain boundaries, while richer native transactions preserve their full change arrays and optional session/base-revision identity. Runtime routes them only to the matching active HostLiveUI `TextInputSession`; text overflow terminates that session with a diagnostic and composition cancellation when possible. Text never becomes gameplay input. The compose witness verifies dead-acute plus `e` commits `é` and that focus/reset clears an incomplete sequence. Runtime witnesses verify broker ordering and multiple committed strings in one HostLiveUI document revision. Run these filtered tests on WSLg:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~XkbComposeStateTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~XkbComposeStateTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~RoutesCommittedPlatformTextToActiveHostLiveUiSession|FullyQualifiedName~PreservesCommittedWaylandTextAfterItsPhysicalKeySequence|FullyQualifiedName~PreservesCommittedWaylandTextBeforeFocusTransitionsWithinCaptureDrain" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~RoutesCommittedPlatformTextToActiveHostLiveUiSession|FullyQualifiedName~PreservesCommittedWaylandTextAfterItsPhysicalKeySequence|FullyQualifiedName~PreservesCommittedWaylandTextBeforeFocusTransitionsWithinCaptureDrain" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

WSLg does not advertise `zwp_text_input_v3`; Wayland and X11 XKB compose report committed-text support without composition. Runtime emits deduplicated `TextCompositionUnavailable` warnings for active sessions and clears them when composition support returns. The Wayland window test verifies its committed-text/no-composition capabilities; the synthetic X11 owner witness verifies the matching Runtime diagnostic and a bounded text-change sample. These tests do not claim physical typing or actual compositor key delivery. XIM/XIC and text-input-v3 preedit, physical event delivery, XInput2 event delivery, live keymap changes, clipboard content-transfer observations, WSI, and Interactive hosting remain unqualified or unimplemented.

The XKB key-name table is hardware-independent and can be tested without an X server:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~XkbKeyNameMapperTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~XkbKeyNameMapperTests"
```

`LinuxEvdevAxisNormalizerTests` exercises literal evdev min/max/flat/fuzz metadata without opening `/dev/input`; this validates normalization only, not device discovery, permissions, or live capture:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~LinuxEvdevAxisNormalizerTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~LinuxEvdevAxisNormalizerTests"
```

In the WSLg shell configured below, run the same hardware-independent selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~LinuxEvdevAxisNormalizerTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~LinuxEvdevAxisNormalizerTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

`LinuxEvdevGamepadButtonStateTests` covers canonical evdev button codes, combined D-pad button/hat state, duplicate suppression, and release generation for device retirement without opening `/dev/input`:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadButtonStateTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadButtonStateTests"
```

In the WSLg shell, run the same hardware-independent selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadButtonStateTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadButtonStateTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

`LinuxEvdevGamepadAxisStateTests` exercises the standard kernel gamepad `ABS_X`/`ABS_Y`, `ABS_RX`/`ABS_RY`, and `ABS_Z`/`ABS_RZ` mappings, Y-axis inversion, radial stick normalization, trigger values, fuzz suppression, and neutral reconciliation state. These hardware-independent cases do not open `/dev/input` or qualify device discovery/live capture:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadAxisStateTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadAxisStateTests"
```

In the WSLg shell, run the same hardware-independent selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadAxisStateTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadAxisStateTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

`LinuxEvdevGamepadStateTests` covers hardware-independent `EV_KEY`/`EV_ABS` translation, button/hat duplicate suppression, ignored key-repeat events, invalid button values, complete neutral reconciliation state, and retirement releases/neutralization with late-event suppression. It does not open `/dev/input` or qualify udev discovery/live capture:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadStateTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadStateTests"
```

In the WSLg shell, run the same hardware-independent selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadStateTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadStateTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

`LinuxEvdevGamepadDiscoveryTests` initializes the systemd `sd-device` input-subsystem monitor, enumerates entries tagged `ID_INPUT_JOYSTICK=1`, and drains bounded hotplug events while validating event-node paths; it does not open nodes. WSLg currently exposes no `/dev/input/event*` nodes, so no add/remove event was observed. This does not qualify hotplug delivery under change, permission failures, or live capture:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadDiscoveryTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadDiscoveryTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

`LinuxEvdevGamepadEventDecoderTests` verifies typed button/axis/stick/trigger samples, host-observation timestamps, monotonic-clock labels, and uncertainty handling for invalid/default native timestamps without opening a device:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadEventDecoderTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadEventDecoderTests"
```

In the WSLg shell, run the same hardware-independent selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadEventDecoderTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadEventDecoderTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

`LinuxEvdevInputEventReaderTests` validates the 64-bit little-endian `input_event` buffer layout and accepts only `/dev/input/event<N>` paths. These cases do not open a node or qualify live reads, permissions, or clock selection:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~LinuxEvdevInputEventReaderTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~LinuxEvdevInputEventReaderTests"
```

In the WSLg shell, run the same hardware-independent selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~LinuxEvdevInputEventReaderTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~LinuxEvdevInputEventReaderTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

`LinuxEvdevGamepadCaptureTests` constructs and pumps the owner-thread capture service through discovery and lifecycle initialization. WSLg currently exposes no `/dev/input/event*` nodes, so it does not observe a real device add/remove or qualify live controller reads and permission handling:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadCaptureTests"
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadCaptureTests"
```

In the WSLg shell, run the same selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~LinuxEvdevGamepadCaptureTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~LinuxEvdevGamepadCaptureTests" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

On Windows 11 x64, run the hidden-HWND owner witness for live client metrics, PMv2 scale/provenance, native resize refresh and snapshot replacement, pre-surface zero epochs, and stale-handle rejection:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.WindowsPlatformOwnerWitnessesHiddenWindowCreationAndRetirement"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.WindowsPlatformOwnerWitnessesHiddenWindowCreationAndRetirement"
```

On Windows 11 x64, verify requested/observed borderless and windowed mode transitions:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.WindowsPlatformOwnerConfirmsBorderlessAndWindowedModeTransitions"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.WindowsPlatformOwnerConfirmsBorderlessAndWindowedModeTransitions"
```

### Offline M04 shader fixture

Run the bounded process and toolchain-manifest regressions. On Windows x64, set `VULKAN_SDK` to the pinned 1.4.328.1 SDK to execute the SPIR-V compile/reflection test; when it is unset, that one test is reported as skipped:

```powershell
dotnet test .\tests\TerraFX.Content.Tools.Tests\TerraFX.Content.Tools.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Content.Tools.Tests\TerraFX.Content.Tools.Tests.csproj --configuration Release
```

On Windows x64, build Content.Tools and verify its copied DXC binary digests, license files, and executable version:

```powershell
dotnet build .\sources\TerraFX.Content.Tools\TerraFX.Content.Tools.csproj --configuration Debug
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll verify-dxc
```

To compile the pinned Windows x64 DXIL fixture and capture `dxc -dumpbin` reflection without the Vulkan SDK:

```powershell
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll compile-m04-dxil .\artifacts\shader-fixtures\m04-offscreen-triangle-dxil
```

This command emits per-stage `.dxil` files and matching `.dxil-reflection.txt` files. It does not validate SPIR-V or make Content.Tools a player runtime dependency.

To compile the M04 offscreen graphics fixture, set `VULKAN_SDK` to the exact Vulkan SDK 1.4.328.1 root containing `Bin\spirv-cross.exe` and `Bin\spirv-val.exe`, then choose a new output directory:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll compile-m04-shader .\artifacts\shader-fixtures\m04-offscreen-triangle
```

The command emits the vertex-ID and Sprite vertex-input stages, `compute-storage`, `compute-storage-buffers`, baseline pixel, resource-free `pixel-graph`, resource-bearing `pixel-sampled` and `pixel-sprite`, combined `pixel-sampled-frame`, and storage-bearing `pixel-sampled-frame-storage` stages as DXIL with DXC reflection dumped from the binary and Vulkan 1.3 SPIR-V with SPIRV-Cross JSON reflection, then validates each SPIR-V module with `spirv-val`. The Sprite stages consume `POSITION` and `TEXCOORD` vertex inputs and sample `FrameTexture` with `FrameSampler`; their paired user locations are checked against both DXIL and SPIRV-Cross reflection. The baseline pixel reflection pins the FrameData UBO to set 0/binding 0; `pixel-graph` is a constant-color pixel stage for native graph draw/readback; `pixel-sampled` consumes `FrameTexture` and `FrameSampler` at set 0 bindings 16 and 40; `pixel-sampled-frame` reflects the UBO together with both sampled resources; `pixel-sampled-frame-storage` also reflects `FrameStorageBuffer` at binding 32 and `FrameStorageImage` at binding 33 with `r32ui` format. `compute-storage` increments one Frame storage-buffer element and one `r32ui` storage-image texel; `compute-storage-buffers` increments separate Frame storage-buffer bindings 0 and 2. Both compute stages pin their 1x1x1 workgroup size from SPIR-V `OpExecutionMode` and DXC `NumThreads` output. The output directory must not already exist. Microsoft DXC is licensed for Windows-only use; Linux Vulkan consumers use the cooked SPIR-V and do not execute the Linux DXC archive.

The typed reflection converter cross-checks SPIRV-Cross uniform-buffer records against DXIL `Buffer Definitions`: record association and byte length, supported flat field names/types/offsets/counts, fixed-array strides, and explicit row-major `float4x4` layout. Nested records, one-element or multidimensional record arrays, and storage-buffer element-record association remain unsupported.

For sampled and storage images, the converter resolves the descriptor-decorated `OpTypeImage` through SPIR-V pointer/array types and compares its supported dimension and 32-bit component class (`f32`, `i32`, or `u32`) with SPIRV-Cross and DXIL resource reflection. For storage images it also cross-checks the SPIR-V image-format enumerant against SPIRV-Cross and DXIL for `r32f`, `r32i`, and `r32ui`; this does not establish sampled texture API view-format compatibility or cover every image shape/format.

For every resource in the supported SPIRV-Cross descriptor collections, the converter compares raw SPIR-V `OpVariable` storage class/resource kind, descriptor set/binding, and the product of fixed array dimensions with SPIRV-Cross metadata; runtime-sized or specialization-sized descriptor arrays reject, and array-count products beyond `Int32.MaxValue` are reported as invalid data. This verifies reflected array extents only, not public descriptor-array element binding semantics.

`ShaderReflection.RequiredCapabilities` includes the selected nonbaseline SPIR-V mappings for geometry, tessellation, Float16, Float64, GroupNonUniform operations, and descriptor indexing, including core dynamic descriptor-array indexing and extension nonuniform/runtime-array capabilities. Baseline `Matrix`/`Shader` declarations are omitted; unsupported capability declarations (including Float16Buffer) reject rather than disappearing from typed reflection. The current GraphicsDevice/backends reject every nonempty capability requirement until a matching device-feature mapping is qualified, so parsed requirements are not execution support.

### Vulkan OffscreenNative backend tests

After compiling the M04 shader fixture, point the Vulkan tests at its output directory. To enable the installed Vulkan validation layer, set its manifest directory and layer name as shown:

```powershell
$env:TERRAFX_M04_SHADER_FIXTURE_DIRECTORY = (Resolve-Path .\artifacts\shader-fixtures\m04-offscreen-triangle).Path
$env:VK_LAYER_PATH = 'C:\VulkanSDK\1.4.328.1\Bin'
$env:VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation'
dotnet test .\tests\TerraFX.Graphics.Vulkan.Tests\TerraFX.Graphics.Vulkan.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Graphics.Vulkan.Tests\TerraFX.Graphics.Vulkan.Tests.csproj --configuration Release
```

Before creating shader modules or pipeline layouts, Vulkan preflight parses the SPIR-V entry point, stage, required Shader and supported Matrix capabilities, descriptor set/register-class bindings, and fixed descriptor-array counts, then compares them to `ShaderReflection`. Compute modules must declare one literal `LocalSize` that matches the reflected workgroup dimensions; specialization-sized workgroups reject. It rejects decoration-group encodings, multi-entrypoint modules, runtime-sized descriptor arrays, combined image-sampler descriptors, and nonbaseline capabilities explicitly; it checks scalar/vector and row-major float4x4 uniform-block member offsets, types, sizes, fixed-array counts and strides, matrix stride, and record bounds. Column-major matrices and matrix forms other than float4x4 with a 16-byte row stride are rejected. Graphics-stage scalar/vector interfaces are checked against actual BuiltIn/Location and value type; interpolation is checked for user Location interfaces. The baseline maps VertexIndex/InstanceIndex/Position/FragCoord/FragDepth and `SV_Target` output indices. Centroid/sample qualifiers and unsupported interface forms reject. User semantic labels are not independently validated beyond the system built-in/location mapping, and BuiltIn interpolation is not represented by SPIR-V interface decorations. Storage-buffer layout, exact trailing block padding, descriptor-array element binding semantics, and sampled API view-format compatibility remain unvalidated by Vulkan preflight; offline conversion cross-checks only the implemented image dimensions/component classes and `r32f`, `r32i`, and `r32ui` storage formats. The native fixture regression also rejects a missing Shader capability without creating a device. This guard runs before the driver pipeline call, so a `t0` versus `t1` metadata mismatch returns an `ArgumentException` instead of reaching native pipeline creation.

Vulkan enumerates and enables `VK_GOOGLE_hlsl_functionality1` and `VK_GOOGLE_user_type` only when advertised; preflight rejects a SPIR-V module declaring an extension that the device did not enable. `fragmentStoresAndAtomics` is enabled and reported through `GraphicsFeature.FragmentStoresAndAtomics` when supported; writable fragment-stage storage pipelines reject before native shader-module creation otherwise.

The baseline does not enable Vulkan `vertexPipelineStoresAndAtomics`; writable vertex-stage storage pipelines reject before native shader-module creation.

Fixture-dependent shader tests compare selected pinned SPIR-V reflection with DXIL and SPIRV-Cross output; they do not establish complete shader equivalence or driver-produced device loss. Runtime graph witnesses cover selected raster, storage, copy, compute, camera-frame, and package-backed Sprite paths. `Renderer.SubmitAsync` reserves one of 1–4 slots before materialization and retains imports, transients, snapshot pins, and pipeline/sampler owners through the actual queue fence; resource retirement waits for both use release and fence completion. D3D12 two-slot texture/buffer alias graphs retain distinct allocations, and host-written Upload sources enable the buffer path without staging. Vulkan delayed buffer/texture alias graphs and exact alias-copy readback pass with validation. Compatible transient-texture alias allocations are cached per device and reused only after all group resources retire; cached bytes remain budget-charged, oldest free entries are evicted under allocation pressure, and the pool drains on clean shutdown. The native Vulkan witness verifies eight sequential allocation reuses and 16 two-live-group waves; fake-backend stress reuses four distinct allocations across 32 exact-budget waves. Current backends use one graphics queue. Graphics.Tests pass 196/196 on Windows and 195/196 on WSLg, with one expected Windows-only consumer skip. Windows graph selections pass 7/7; WSLg passes four Vulkan cases with three expected D3D12 skips and no validation errors. Staging-dependent D3D12 cases still refuse at the observed zero non-local budget; WSLg uses CPU llvmpipe. Broader shader/resource shapes, arbitrary graph-copy topologies, multi-queue retirement, built-in Renderer Sprite pipeline/draw, Surface/WSI, and Interactive hosting remain open. Fixture-dependent tests require the shader environment to be set; empty selections are not evidence.

Run the public offscreen submission witness after compiling the fixture. It covers resource-free, Frame-UBO, sampled-image/sampler, combined-draw, selected storage-buffer/storage-image `ReadWrite`, buffer copy, compute storage-buffer/image, two-buffer compute, transient-buffer, and transient buffer/texture alias variants. The camera-frame case reads back gray and then black after the World camera changes and a second snapshot is extracted; the first snapshot remains unchanged. Storage barriers make dependent buffer/texel updates read back as 4 then 6; compute dispatches read back exact outputs 2 and 4; copy and alias cases verify exact bytes. D3D12 and Vulkan use native UAV/state or synchronization2 barriers. `Renderer.SubmitAsync` retains slot command storage, imports, transients, snapshot pins, and recorded pipeline/sampler owners through the actual queue fence. Graph submissions and synchronous uploads/readbacks record each resource's queue fence; retirement requires inactive uses and observed completion. The D3D12 two-slot witness holds two native fence waits in flight, verifies third-slot backpressure/cancellation, and proves two transient texture alias groups have distinct per-submission allocations retained until completion; it passes 1/1 in Windows Debug/Release. `WriteUploadBufferAsync` writes to an idle host-visible Upload buffer on the graphics owner, without a staging allocation or GPU-fence claim. The D3D12 two-slot buffer graph uses those upload-heap sources to submit two transient-buffer alias groups, verifies distinct allocations remain live through both queue fences, and passes Windows Debug/Release. The Vulkan witness aliases two sequential transient textures while copying the gray offscreen target into an imported destination and verifies exact sampled pixels; Windows and WSLg Debug/Release pass with no WSLg VUID diagnostics. Delayed Vulkan witnesses submit separate-target graphs with transient-buffer and transient-texture alias groups across live slots, verify distinct per-graph allocations remain live, then confirm release after both fences; Windows and WSLg Debug/Release pass with no WSLg validation errors. Current backends share one graphics queue; multi-queue vectors remain open. The latest Windows graph matrix still has eleven D3D12 cases refusing because their separate 64-KiB staging allocations exceed the queried zero-byte non-local budget; the new upload-heap buffer witness does not use staging. WSLg uses CPU-software Mesa llvmpipe, not hardware-GPU qualification. Vulkan also uploads the color target before the first graph to exercise `GENERAL`-to-color-attachment synchronization. Broader compute/resource shapes, arbitrary graph-copy topologies, complete snapshot-to-GPU composition, Surface/WSI, and Interactive hosting remain unimplemented:

`RenderSpriteGeometryTests` verifies the documented pivoted local-quad positions, fixed clockwise indices, normalized Atlas-frame UVs, and horizontal/vertical UV flips. `SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload` verifies extraction stores geometry composed from the exact pinned Atlas frame and copied Sprite component values.

`Renderer.SubmitAsync` retains an independent shared asset-pin reference through GPU completion; the Sprite snapshot test disposes its snapshot and verifies the retained Atlas page and Material remain available.

`SnapshotPinnedAtlasPageSamplesThroughNativeGraphAsync` uses a committed Scene/Sprite snapshot, resolves its exact Atlas frame page through `Renderer.EnsureSpritePageResidencyAsync`, uploads the four copied quad vertices and fixed six-element UInt16 index sequence with their captured Atlas UVs, and samples the page in an indexed custom graph draw. It verifies both the rendered quad and an untouched clear pixel, then unloads the Scene and disposes the snapshot before verifying the retained texture readback. The lifetime regression also disposes the imported vertex and index buffers immediately after starting `Renderer.SubmitAsync`; submission pins the output target and imported resources before its first asynchronous wait. A bounded slot is reserved before transient materialization and callback recording, and remains held with the output/imported-resource use leases until the native fence is observed. Callback failure and cancellation during recording before graph admission both release their slot and resource-use leases. This is native graph use of the captured Sprite data, not a built-in Renderer Sprite pipeline or batching path.

Run the hardware-independent Sprite quad and snapshot-copy cases without the shader fixture:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~RenderSpriteGeometryTests|FullyQualifiedName~SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~RenderSpriteGeometryTests|FullyQualifiedName~SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload"
```

In the WSLg shell, run the same selection:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~RenderSpriteGeometryTests|FullyQualifiedName~SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~RenderSpriteGeometryTests|FullyQualifiedName~SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

The transient-texture alias witness chains two exact-description textures through disjoint copy-pass lifetimes. `Renderer.SubmitAsync` materializes the pair from one shared allocation and records the alias handoff; D3D12 uses placed textures and an aliasing barrier, while Vulkan binds alias-capable images to shared memory and records a synchronization2 memory barrier. Windows D3D12/Vulkan Debug/Release pass the focused texture-copy/alias selections; WSLg Vulkan passes with Khronos validation and skips the expected D3D12 cases. The overlapping-lifetime test verifies separate groups and no alias barrier. `Graphics.Tests` also holds four transient-buffer groups and four transient-texture groups concurrently under an exact four-allocation fake-backend budget, confirms distinct allocation leases per live group, rejects a fifth allocation, and verifies budget recovery after releasing a retained use. The focused two-test selection passes on Windows and WSLg in Debug and Release. The Vulkan host-issued-device witness reserves two native frame slots while holding separate transient buffer and texture alias groups, verifying distinct allocation leases; Windows and WSLg Debug/Release pass. `D3D12GraphSubmissionRetainsTransientAliasGroupsPerSlot` holds two submitted Renderer graphs whose transient textures alias within each graph, verifies the two in-flight submissions own separate native allocations, and waits for fence-backed release; Windows Debug/Release pass without staging. `SubmitsTransientCopyAliasGroupsOnVulkan` executes the same target-to-transient-to-destination copy chain and verifies output pixels in Windows/WSLg Debug/Release with no WSLg VUID or Validation Error. Buffer-transient overlap across slots and delayed overlapping Vulkan graph submissions remain open.

```powershell
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug --filter "FullyQualifiedName~ConcurrentTransientBufferGroupsRespectBudgetAndRecoverAfterUseRelease|FullyQualifiedName~ConcurrentTransientTextureGroupsRespectBudgetAndRecoverAfterUseRelease"
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Release --filter "FullyQualifiedName~ConcurrentTransientBufferGroupsRespectBudgetAndRecoverAfterUseRelease|FullyQualifiedName~ConcurrentTransientTextureGroupsRespectBudgetAndRecoverAfterUseRelease"
```

```powershell
$env:TERRAFX_M04_SHADER_FIXTURE_DIRECTORY = (Resolve-Path .\artifacts\shader-fixtures\m04-offscreen-triangle).Path
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SubmitsOffscreenClearGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SubmitsOffscreenClearGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SubmitsPackageBackedTextureGraphOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SubmitsPackageBackedTextureGraphOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SnapshotPinnedAtlasPageBecomesResidentAfterSceneAndSnapshotRetireAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SnapshotPinnedAtlasPageBecomesResidentAfterSceneAndSnapshotRetireAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SnapshotPinnedAtlasPageSamplesThroughNativeGraphAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SnapshotPinnedAtlasPageSamplesThroughNativeGraphAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SnapshotGraphCancellationReleasesImportedResourcePinsAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SnapshotGraphCancellationReleasesImportedResourcePinsAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SnapshotGraphRecordingFailureReleasesImportedResourcePinsAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SnapshotGraphRecordingFailureReleasesImportedResourcePinsAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "Name~D3D12GraphSubmissionRetainsTargetUntilFenceCompletion"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "Name~D3D12GraphSubmissionRetainsTargetUntilFenceCompletion"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "Name~D3D12GraphSubmissionRetainsTransientAliasGroupsPerSlot|Name~SubmitsTransientCopyAliasGroupsOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "Name~D3D12GraphSubmissionRetainsTransientAliasGroupsPerSlot|Name~SubmitsTransientCopyAliasGroupsOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "Name~D3D12GraphAllocatesTransientBufferAliasGroupsAcrossReservedSlots"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "Name~D3D12GraphAllocatesTransientBufferAliasGroupsAcrossReservedSlots"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "Name~D3D12SubmitsTransientBufferAliasGroupsAcrossLiveSlots"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "Name~D3D12SubmitsTransientBufferAliasGroupsAcrossLiveSlots"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~D3D12TransientTextureAllocationPoolReusesAfterRetirement"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~D3D12TransientTextureAllocationPoolReusesAfterRetirement"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "Name~SubmitsTransientBufferAliasGraphFromUploadBufferOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "Name~SubmitsTransientBufferAliasGraphFromUploadBufferOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "Name~SubmitsTransientBufferAliasGroupsAcrossLiveSlotsOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "Name~SubmitsTransientBufferAliasGroupsAcrossLiveSlotsOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "Name~SubmitsTransientTextureAliasGroupsAcrossLiveSlotsOnVulkan"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "Name~SubmitsTransientTextureAliasGroupsAcrossLiveSlotsOnVulkan"
```

`CommandRecorder.CopyTexture` copies one complete imported 2D color mip/layer between distinct textures with matching format and mip extent. Its `TextureCopyRegion` overload copies a source rectangle that fills the complete destination mip; the imported RGBA8 witness copies a 2x2 crop at offset (1,1) from a 4x4 texture into a 2x2 destination and verifies every output byte. The full-copy witnesses also cover mip 1/layer 1 from 4x4 two-mip, two-layer textures, transient intermediates, two independent texture copies in one graph, and mixed buffer/texture commands in one pass. Windows Debug/Release pass the partial-region D3D12 and Vulkan witnesses; WSLg Debug/Release pass Vulkan with Khronos validation and skip the D3D12 case. Run the owning Runtime selections in Debug/Release on Windows and Vulkan in the WSLg lane:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SubmitsTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SubmitsTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SubmitsPartialTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SubmitsPartialTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SubmitsTransientTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SubmitsTransientTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~PlansOverlappingTransientTextureLifetimesSeparatelyOnVulkan|FullyQualifiedName~PlansSequentialTransientTextureAliasGroupsAndSubmitsAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~PlansOverlappingTransientTextureLifetimesSeparatelyOnVulkan|FullyQualifiedName~PlansSequentialTransientTextureAliasGroupsAndSubmitsAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SubmitsMultipleTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SubmitsMultipleTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SubmitsCombinedBufferAndTextureCopyGraphAndReadsBackAsync"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SubmitsCombinedBufferAndTextureCopyGraphAndReadsBackAsync"
```

For WSLg, set `WORKTREE` to the checkout's Linux path and run outside the checkout so the Linux-local SDK is selected instead of the Windows `.dotnet` candidate in `global.json`:

```bash
export WORKTREE=/mnt/d/path/to/worktree
cd /tmp
export DOTNET_ROOT="$WORKTREE/artifacts/linux/dotnet"
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_MULTILEVEL_LOOKUP=0
export LD_LIBRARY_PATH="$WORKTREE/artifacts/linux/native/extracted/usr/lib/x86_64-linux-gnu"
export VK_LAYER_PATH="$WORKTREE/artifacts/linux/native/extracted/usr/share/vulkan/explicit_layer.d"
export VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation
export TERRAFX_M04_SHADER_FIXTURE_DIRECTORY=
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~VulkanSurfaceFormatSelectorTests|FullyQualifiedName~VulkanBackend" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~VulkanSurfaceFormatSelectorTests|FullyQualifiedName~VulkanBackend" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~SubmitsTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SubmitsTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~SubmitsPartialTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SubmitsPartialTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~SubmitsTransientTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SubmitsTransientTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~PlansOverlappingTransientTextureLifetimesSeparatelyOnVulkan|FullyQualifiedName~PlansSequentialTransientTextureAliasGroupsAndSubmitsAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~PlansOverlappingTransientTextureLifetimesSeparatelyOnVulkan|FullyQualifiedName~PlansSequentialTransientTextureAliasGroupsAndSubmitsAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~SubmitsMultipleTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SubmitsMultipleTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~SubmitsCombinedBufferAndTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SubmitsCombinedBufferAndTextureCopyGraphAndReadsBackAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "Name~SubmitsTransientCopyAliasGroupsOnVulkan" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "Name~SubmitsTransientCopyAliasGroupsOnVulkan" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "Name~SubmitsTransientBufferAliasGroupsAcrossLiveSlotsOnVulkan" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "Name~SubmitsTransientBufferAliasGroupsAcrossLiveSlotsOnVulkan" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "Name~SubmitsTransientTextureAliasGroupsAcrossLiveSlotsOnVulkan" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "Name~SubmitsTransientTextureAliasGroupsAcrossLiveSlotsOnVulkan" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Graphics.Tests/TerraFX.Graphics.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~ConcurrentTransientBufferGroupsRespectBudgetAndRecoverAfterUseRelease|FullyQualifiedName~ConcurrentTransientTextureGroupsRespectBudgetAndRecoverAfterUseRelease|FullyQualifiedName~TransientTextureAllocationPoolReusesAllocationsAcrossConcurrentWaves" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Graphics.Tests/TerraFX.Graphics.Tests.csproj" --configuration Release --filter "FullyQualifiedName~ConcurrentTransientBufferGroupsRespectBudgetAndRecoverAfterUseRelease|FullyQualifiedName~ConcurrentTransientTextureGroupsRespectBudgetAndRecoverAfterUseRelease|FullyQualifiedName~TransientTextureAllocationPoolReusesAllocationsAcrossConcurrentWaves" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

Run the four-slot Vulkan Sprite graph and graph-failure lease-release witnesses in WSLg with the cooked Sprite shader fixture:

```bash
export TERRAFX_M04_SHADER_FIXTURE_DIRECTORY="$WORKTREE/artifacts/shader-fixtures/m04-offscreen-sprite-quad"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~SubmitsPackageBackedTextureGraphOnVulkan" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SubmitsPackageBackedTextureGraphOnVulkan" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~SnapshotGraphCancellationReleasesImportedResourcePinsAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SnapshotGraphCancellationReleasesImportedResourcePinsAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~SnapshotGraphRecordingFailureReleasesImportedResourcePinsAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SnapshotGraphRecordingFailureReleasesImportedResourcePinsAsync" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

### Vulkan GraphicsDevice startup through the host issuer

This focused Runtime test supplies a real `PlatformHostOwner`-issued identity to `GraphicsDevice.CreateAsync`, starts an OffscreenNative device through `VulkanGraphicsBackend.CreateRegistration`, records the reported adapter/driver/API and validation-layer environment in the TRX output, then uses `CreateBufferAsync`, `UploadBufferAsync`, and `ReadbackBufferAsync` to verify an exact 32-byte round trip before shutting down the device and platform owner. It also compiles eight sequential transient buffers and eight sequential transient textures into separate compatible allocation groups with seven alias handoffs each, allocates and retires each group eight times, then holds two frame-slot reservations while repeatedly materializing and retiring two distinct native texture alias groups for 16 waves; the buffer pair and every texture pair must retain distinct shared-allocation leases:

```powershell
$env:VULKAN_SDK = 'C:\VulkanSDK\1.4.328.1'
$env:VK_LAYER_PATH = 'C:\VulkanSDK\1.4.328.1\Bin'
$env:VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation'
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~EngineHostOptionsTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~EngineHostOptionsTests"
```

This verifies the internal startup port and public buffer-resource request path with the sealed `GraphicsDevice` facade; it does not exercise `EngineHost`, Persistence service initialization, texture/sampler/pipeline requests through the facade, or graph submission. The test explicitly skips when the Vulkan loader or a qualifying device is unavailable.

### Retained AssetLease lifetime

Run the focused retained-handle, lease-capacity, package-replacement, and store-disposal regressions:

```powershell
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AssetStoreRetain"
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Release --filter "FullyQualifiedName~AssetStoreRetain"
```

### Package-bound AssetLease loading

Run the regression proving referenced assets are loaded from the lease's original package after replacement:

```powershell
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AssetStoreLoadsAssetFromPackageLease"
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Release --filter "FullyQualifiedName~AssetStoreLoadsAssetFromPackageLease"
```

### Scene prefab package continuity

Run the Scene expansion regression proving nested Prefabs remain bound to the root Scene lease's package after the AssetStore is replaced:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~ScenePrefabExpansionUsesRootSceneLeaseBackingAfterStoreReplacement"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~ScenePrefabExpansionUsesRootSceneLeaseBackingAfterStoreReplacement"
```

### Rendering texture asset codec

Run the package-backed RGBA8 texture codec regressions for exact BaseOnly/FullChain mips and semantic validation:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~TextureAssetCodec"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~TextureAssetCodec"
```

### Rendering Atlas page loading

Run the package-backed Atlas tests for same-package page resolution across replacement, texture metadata, clamped two-texel borders, and padded-frame overlap:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AtlasPageSet"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~AtlasPageSet"
```

### Rendering Material profiles and texture dependencies

Run the package-backed Sprite/PBR Material tests for profile tuples, parameter and render-state constraints, typed texture bindings, slot semantics, and same-package dependency loading across replacement:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~MaterialAssetCodec|FullyQualifiedName~MaterialTextureLoader"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~MaterialAssetCodec|FullyQualifiedName~MaterialTextureLoader"
```

### Scene Sprite component source

Run the package-backed Scene activation test for required Atlas references, optional Material references, Sprite value validation, and refusal to synthesize an absent required Atlas/Frame:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation"
```

### Prepared Sprite resource ownership

Run the package-backed Sprite resource tests for exact root-Scene package loading, page validation, SceneInstance ownership, and package replacement:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SpriteAssetPreparationUsesTheRootScenePackageAfterStoreReplacement|FullyQualifiedName~SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SpriteAssetPreparationUsesTheRootScenePackageAfterStoreReplacement|FullyQualifiedName~SceneActivationRestoresSpriteAssetReferencesAndRejectsInvalidMutation"
```

### Sprite snapshot extraction and retained pins

Run the 2D/3D extraction regression for stable per-camera Sprite ordering, empty camera-mask selection without asset pins, real committed-frame transforms, and Atlas/Material/page pins that remain valid after Scene unload and release with snapshot disposal:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SpriteSnapshotsOrderDrawsAndRetainAssetsAfterSceneUnload"
```

### Public structural EBIN and schema metadata

Core exposes bounded schema-agnostic EBIN reader/writer and scalar/array payload helpers. These validate wire structure and preserve canonical byte layout; they do not activate or validate an owner schema. `CompiledContentSchemaRegistry` contains immutable schema/profile metadata only. Typed owner codecs and their roots are separately compiled with the selected application registrations.

Run the focused structural, payload, and registry metadata regressions:

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~EbinStructuralReaderTests|FullyQualifiedName~EbinStructuralWriterTests|FullyQualifiedName~EbinPayloadWriterTests|FullyQualifiedName~ContentSchemaRegistryTests"
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~EbinStructuralReaderTests|FullyQualifiedName~EbinStructuralWriterTests|FullyQualifiedName~EbinPayloadWriterTests|FullyQualifiedName~ContentSchemaRegistryTests"
```

These tests establish only the behaviors they exercise; schema-owner semantics, World registration, package loading, persistence, and M03 acceptance remain distinct.

### Core-private foundation codecs

The EBIN1 structural layer and M02 foundation admission layer live in `sources\TerraFX.Core`.
They are internal, BCL-only implementation details, not a public codec API or an Assets dependency.
The literal eight-schema catalogue fixes canonical registry/kernel bytes and digests; schema versions
own their fixed behavior, and incoming manifests cannot replace that catalogue. Profile admission uses
an explicit selection of already registered identities/settings. Optional extensions use the reserved
EBIN extension set with signed 64-bit IDs and inert opaque payloads. Frame I/O additionally requires a private persistence binding
containing the stable world GUID, profile, runtime owner and binding revision.

Reads validate bounded spans before copying an owned staged wire value. They neither allocate a
runtime owner nor register profiles or activate worlds. Projection and capture are explicit operations
against an owner binding; retained optional fields remain inert and survive recapture. Writers validate
input and checked extent before destination capacity, leave the entire destination unchanged on failure,
and report zero written. Structural payload I/O does not substitute for schema/context admission.

Run the focused foundation-codec regressions:

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~M02FoundationCodecTests"
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~M02FoundationCodecTests"
```

The tests construct five canonical EBIN records in C# and check their exact lengths and SHA-256 values.
Focused cases exercise distinct codec, validation, binding and ownership behavior; there is no separate
M02 fixture manifest, materializer, prefix corpus or fixture-package pin to maintain. The test project
also builds the separate unsafe-disabled public spatial consumer.

The spatial consumer can be run separately as JIT or published for a Windows x64 NativeAOT execution:

```powershell
dotnet run --project .\tests\TerraFX.Core.SpatialConsumer\TerraFX.Core.SpatialConsumer.csproj --configuration Release
dotnet publish .\tests\TerraFX.Core.SpatialConsumer\TerraFX.Core.SpatialConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
```

This consumer covers its selected spatial API surface only; it does not complete the M02 NativeAOT numerical profiles or Linux target qualification.

### Focused production behavior tests

The Core suite contains focused literal cases for bounded rounding, spatial conversion/normalization,
Angle, and geometry behavior. Physics primitive tests exercise the production validators and measures;
these selections do not imply unimplemented M03 package, World, native, or AOT behavior.

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~M02FoundationCodecTests|FullyQualifiedName~BoundedRationalTests|FullyQualifiedName~SpatialMathTests|FullyQualifiedName~SpatialValueTests|FullyQualifiedName~AngleTests|FullyQualifiedName~GeometryKernelTests|FullyQualifiedName~GeometryComparisonTests|FullyQualifiedName~ContentSchemaReferenceTests"
dotnet test .\tests\TerraFX.Physics.Tests\TerraFX.Physics.Tests.csproj --configuration Debug --filter "FullyQualifiedName~PrimitiveShapeValidationTests"
```

Repeat the applicable selections in Release after Debug succeeds. Confirm nonzero selected and executed counts in current-run TRX results under `artifacts\tst\<Configuration>`.

### Runtime public-provider consumers

The Runtime test project builds two independent unsafe-disabled executable witnesses: the original handwritten component consumer and the generated-component consumer. The generated consumer references `TerraFX.Generators` as an analyzer and exercises the same public schema registration, World mutation, default, and scoped-query path without changing the handwritten source.

Run each executable separately after building:

```powershell
dotnet run --project .\tests\TerraFX.Runtime.ComponentConsumer\TerraFX.Runtime.ComponentConsumer.csproj --configuration Debug
dotnet run --project .\tests\TerraFX.Runtime.GeneratedComponentConsumer\TerraFX.Runtime.GeneratedComponentConsumer.csproj --configuration Debug
```

The Runtime suite also runs an unsafe-disabled Scene activation witness through the handwritten consumer as a separate process. It covers an encoded optional Entity reference bound explicitly to null and rejects the same null target for a required reference:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SceneLoaderActivationHandlesExplicitNullEntityReference"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SceneLoaderActivationHandlesExplicitNullEntityReference"
```

By default, these focused tests run the handwritten consumer through JIT. They do not complete Scene hierarchy transactions or full CPU extraction/native qualification.

The Scene owner-boundary regression also checks that the estimated expansion bytes are reserved before materialization, remain charged after activation, and are released on unload or when a prepared plan is abandoned. The admission-domain case checks the per-World ceiling under concurrent reservation attempts:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~WorldReservationGrowthCannotExceedItsOwnedStateCeiling|FullyQualifiedName~SceneLoaderActivatesAtTheWorldOwnerControlBoundary|FullyQualifiedName~SceneStorageReservationsAreThreadSafeAndRespectTheWorldBudget"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorldReservationGrowthCannotExceedItsOwnedStateCeiling|FullyQualifiedName~SceneLoaderActivatesAtTheWorldOwnerControlBoundary|FullyQualifiedName~SceneStorageReservationsAreThreadSafeAndRespectTheWorldBudget"
```

These reservation and activation regressions do not qualify NativeAOT or native execution.

The SceneInstance transaction regressions cover preview and owner-boundary revalidation, same-World cross-instance parents, foreign/stale targets, sequential root transitions, atomic cycle rejection, KeepWorld residual refusal, cancellation, and queue-capacity refusal:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~SceneAssetStoreTests.SceneTransaction"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SceneAssetStoreTests.SceneTransaction"
```

These managed transaction cases do not qualify CPU extraction, NativeAOT, or native execution.

The headless extraction cases exercise package-backed Scene and nested-Prefab activation, exact committed 2D/3D frame selection, canonical camera ordering, hierarchical pose composition, directional/point/spot light snapshots, camera-layer filtering, stable identities, exact combined light-capacity refusal, copied snapshot lifetime, and stale-revision rejection. They also verify light defaults/validation and that CPU extraction registration requires Scene support and is admitted only once:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~HeadlessRendererExtractsNestedSceneCamerasFromOneCommittedFrame|FullyQualifiedName~CpuExtractionSupportRequiresSceneSupportAndRejectsDuplicateRegistration|FullyQualifiedName~DirectionalAndSpotLightsUseWorldDefaultsAndRejectInvalidMutation|FullyQualifiedName~PointLightUsesWorldDefaultsAndRejectsInvalidMutation"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~HeadlessRendererExtractsNestedSceneCamerasFromOneCommittedFrame|FullyQualifiedName~CpuExtractionSupportRequiresSceneSupportAndRejectsDuplicateRegistration|FullyQualifiedName~DirectionalAndSpotLightsUseWorldDefaultsAndRejectInvalidMutation|FullyQualifiedName~PointLightUsesWorldDefaultsAndRejectsInvalidMutation"
```

This covers point lights only; draw/sprite/mesh/bone sources, directional/spot lights, asset-lease retention, and the complete frame-cohort lifecycle remain unimplemented. These managed tests do not qualify NativeAOT or native rendering.

For Windows NativeAOT qualification, each executable project enables `PublishAot` locally when `TerraFXPublishAot=true`; the generated project's analyzer reference removes that switch. Publish and execute the artifacts separately:

```powershell
dotnet publish .\tests\TerraFX.Runtime.ComponentConsumer\TerraFX.Runtime.ComponentConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Runtime.GeneratedComponentConsumer\TerraFX.Runtime.GeneratedComponentConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Graphics.D3D12.Consumer\TerraFX.Graphics.D3D12.Consumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Runtime.Vulkan.Consumer\TerraFX.Runtime.Vulkan.Consumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
```

The D3D12 consumer enables NativeAOT locally through `TerraFXPublishAot`; do not pass `PublishAot=true` as a global MSBuild property, which would also apply AOT to build-time analyzer/generator projects. Its no-argument mode verifies the nonfriend public registration and transfer/pipeline/sampler signatures, constructs canonical `ShaderReflection` resource/layout/capability values, and proves the reflected value independently copies caller-owned arrays and compiler digest. Its `interactive` mode starts WindowsNative and D3D12, creates two hidden Win32 windows and Surfaces, activates the supplied camera Scene, presents to both, advances the hosted World by one committed tick, closes one window, and presents the new snapshot on the surviving Surface. It also verifies that a throwing `EngineHost.InvokeAsync` callback returns its exception and a later callback can access both Surfaces. Windows JIT consumers enable built-in COM interop for GameInputSharp. Under NativeAOT, GameInputSharp's `[ComImport]` adapter requires runtime-generated COM stubs, so gamepad capture is reported unavailable with a diagnostic and no XInput fallback; this does not qualify physical gamepad input.

```powershell
& .\artifacts\bin\tests\TerraFX.Graphics.D3D12.Consumer\Release\net11.0\win-x64\publish\TerraFX.Graphics.D3D12.Consumer.exe
```

Run the published D3D12 Interactive presentation witness:

```powershell
$env:TERRAFX_D3D12_NATIVEAOT_CONSUMER = (Resolve-Path .\artifacts\bin\tests\TerraFX.Graphics.D3D12.Consumer\Release\net11.0\win-x64\publish\TerraFX.Graphics.D3D12.Consumer.exe).Path
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~PublishedD3D12InteractiveConsumerPresentsAcrossTwoWindows"
Remove-Item Env:\TERRAFX_D3D12_NATIVEAOT_CONSUMER
```

The Windows x64 NativeAOT Interactive selection passes 1/1 on the current D3D12 device. It advances the hosted World, presents across two windows, closes one, and verifies that the other presents a newer snapshot. It does not qualify driver-generated loss/recovery, WSI timeout behavior, or physical gamepad input.

The Vulkan consumer also supports Windows Win32 Interactive presentation. Run the exact published NativeAOT executable through the scenario driver:

```powershell
$env:TERRAFX_VULKAN_NATIVEAOT_CONSUMER = (Resolve-Path .\artifacts\bin\tests\TerraFX.Runtime.Vulkan.Consumer\Release\net11.0\win-x64\publish\TerraFX.Runtime.Vulkan.Consumer.exe).Path
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~PublishedVulkanConsumerExecutesScenario"
Remove-Item Env:\TERRAFX_VULKAN_NATIVEAOT_CONSUMER
```

On Windows, Win32 Interactive and OffscreenNative pass 2/2; Linux Wayland/X11 are skipped as OS-inapplicable. The Win32 path covers two windows, one committed World tick, peer-window close, presentation on the surviving Surface, and callback exception recovery. OffscreenNative verifies both the graph-clear pixel and a padded 3D R8 upload/readback. GameInput remains unavailable under NativeAOT as described above.

The Windows direct-Surface stress runs 100 resize/minimize/restore requests against D3D12 and Vulkan Win32:

```powershell
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~D3D12BackendCreatesAndCompilesWin32FlipDiscardSurface|FullyQualifiedName~VulkanBackendRunsOneHundredResizeMinimizeRestoreOperationsOnWindows"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~D3D12BackendCreatesAndCompilesWin32FlipDiscardSurface|FullyQualifiedName~VulkanBackendRunsOneHundredResizeMinimizeRestoreOperationsOnWindows"
```

The no-argument handwritten Runtime consumer verifies that `PhysicalInputEvent` and `PhysicalInputValue` expose no public constructors and that event/value/diagnostic/scroll-unit enum values are stable. Its JIT and Windows x64 NativeAOT executions validate that nonfriend surface only; they do not activate native input or create a platform window.

The Scene activation public-consumer cases exercise both standalone World activation and the Headless `EngineHost`/hosted-World path. The hosted case uses the host-owned AssetStore, EngineContext World creation, the public frame/time-scale request APIs, owner pump, activation, and shutdown. Run the exact cases through the published handwritten NativeAOT executable:

```powershell
$env:TERRAFX_SCENE_ACTIVATION_CONSUMER = (Resolve-Path .\artifacts\bin\tests\TerraFX.Runtime.ComponentConsumer\Release\net11.0\win-x64\publish\TerraFX.Runtime.ComponentConsumer.exe).Path
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Release --filter "FullyQualifiedName~SceneLoaderActivationHandlesExplicitNullEntityReference"
Remove-Item Env:\TERRAFX_SCENE_ACTIVATION_CONSUMER
```

When set, the test launches that exact executable and does not fall back to JIT if it fails.

The public consumer binds `EngineHost.RecoverGraphicsAsync` to `Func<CancellationToken, Task>` to compile-check its public signature. It does not invoke recovery because Headless has no GraphicsDevice.

NativeAOT does not support cross-OS compilation. On WSLg, the Linux GCC 15 toolchain is privately extracted under `artifacts/linux/native/gcc-toolchain`; no system packages are installed. For a fresh WSLg workspace, stage GCC and its development dependencies without system installation:

```bash
TOOLCHAIN="$WORKTREE/artifacts/linux/native/gcc-toolchain"
mkdir -p "$TOOLCHAIN/packages" "$TOOLCHAIN/root" "$TOOLCHAIN/bin"
cd "$TOOLCHAIN/packages"
apt-get download gcc-15 gcc-15-x86-64-linux-gnu cpp-15 cpp-15-x86-64-linux-gnu gcc-15-base libcc1-0 binutils-x86-64-linux-gnu libgcc-15-dev libc6 libc6-dev libc-dev-bin linux-libc-dev rpcsvc-proto libgcc-s1 libgomp1 libitm1 libatomic1 libasan8 liblsan0 libtsan2 libubsan1 libhwasan0 libquadmath0 libgmp10 libisl23 libmpc3 libmpfr6 libstdc++6 libzstd1 zlib1g
for package in ./*.deb; do dpkg-deb -x "$package" "$TOOLCHAIN/root"; done
export TERRAFX_GCC_ROOT="$TOOLCHAIN/root"
printf '%s\n' '#!/bin/sh' ': "${TERRAFX_GCC_ROOT:?set TERRAFX_GCC_ROOT}"' 'exec "$TERRAFX_GCC_ROOT/usr/bin/x86_64-linux-gnu-gcc-15" --sysroot="$TERRAFX_GCC_ROOT" "$@"' > "$TOOLCHAIN/bin/gcc"
chmod +x "$TOOLCHAIN/bin/gcc"
export PATH="$TOOLCHAIN/bin:$TERRAFX_GCC_ROOT/usr/bin:$PATH"
export GCC_EXEC_PREFIX="$TERRAFX_GCC_ROOT/usr/lib/gcc/"
export CPATH="$TERRAFX_GCC_ROOT/usr/include:$TERRAFX_GCC_ROOT/usr/include/x86_64-linux-gnu"
export C_INCLUDE_PATH="$CPATH"
export LIBRARY_PATH="$TERRAFX_GCC_ROOT/usr/lib/x86_64-linux-gnu:$TERRAFX_GCC_ROOT/lib/x86_64-linux-gnu:$TERRAFX_GCC_ROOT/usr/lib/gcc/x86_64-linux-gnu/15"
export LD_LIBRARY_PATH="$WORKTREE/artifacts/linux/native/extracted/usr/lib/x86_64-linux-gnu:$TERRAFX_GCC_ROOT/usr/lib/x86_64-linux-gnu:$TERRAFX_GCC_ROOT/lib/x86_64-linux-gnu:$TERRAFX_GCC_ROOT/usr/lib/gcc/x86_64-linux-gnu/15${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
```

Publish and execute the unsafe-disabled handwritten and generated consumers with the pinned Linux SDK:

```bash
"$DOTNET_ROOT/dotnet" publish "$WORKTREE/tests/TerraFX.Runtime.ComponentConsumer/TerraFX.Runtime.ComponentConsumer.csproj" --configuration Release --runtime linux-x64 --self-contained true -p:TerraFXPublishAot=true "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" publish "$WORKTREE/tests/TerraFX.Runtime.GeneratedComponentConsumer/TerraFX.Runtime.GeneratedComponentConsumer.csproj" --configuration Release --runtime linux-x64 --self-contained true -p:TerraFXPublishAot=true "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$WORKTREE/artifacts/linux/bin/tests/TerraFX.Runtime.GeneratedComponentConsumer/Release/net11.0/linux-x64/publish/TerraFX.Runtime.GeneratedComponentConsumer"
TERRAFX_SCENE_ACTIVATION_CONSUMER="$WORKTREE/artifacts/linux/bin/tests/TerraFX.Runtime.ComponentConsumer/Release/net11.0/linux-x64/publish/TerraFX.Runtime.ComponentConsumer" "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~SceneLoaderActivationHandlesExplicitNullEntityReference" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

Both Linux x64 NativeAOT consumers execute successfully; the Headless Scene activation selection passes 4/4 with the exact published executable. This does not qualify Interactive hosting, graphics presentation/WSI, physical input, or Linux GPU hardware. JIT or Windows NativeAOT execution does not qualify the Linux target.

The unsafe-disabled Vulkan consumer exercises a package-backed camera Scene through OffscreenNative readback and Interactive Surface presentation. OffscreenNative verifies a graph-clear pixel and a padded 3D R8 upload/readback. Each Wayland-only and explicit X11 Interactive scenario opens two hidden windows, presents to both, advances the hosted World by one committed tick, closes one window, presents the new snapshot on the survivor, and closes both windows. With the WSLg SDK/Vulkan environment configured above, publish it and run all three paths:

```bash
"$DOTNET_ROOT/dotnet" publish "$WORKTREE/tests/TerraFX.Runtime.Vulkan.Consumer/TerraFX.Runtime.Vulkan.Consumer.csproj" --configuration Release --runtime linux-x64 --self-contained true -p:TerraFXPublishAot=true "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/" -m:1 -nr:false
TERRAFX_VULKAN_NATIVEAOT_CONSUMER="$WORKTREE/artifacts/linux/bin/tests/TerraFX.Runtime.Vulkan.Consumer/Release/net11.0/linux-x64/publish/TerraFX.Runtime.Vulkan.Consumer" "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~PublishedVulkanConsumerExecutesScenario" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/" -m:1 -nr:false
```

This selection passes the three Linux scenarios in Debug JIT and Release NativeAOT; the Windows-only Win32 case is skipped on WSLg. The passing paths are Vulkan OffscreenNative readback plus Wayland and explicit X11 Interactive presentation, each Interactive path covering two windows, one committed World tick, peer-window close, presentation on the surviving Surface, and callback exception recovery. It qualifies WSLg on the observed llvmpipe software adapter, not hardware GPU behavior.

The WSLg Surface stress runs 100 resize/minimize/restore requests on Wayland and X11. Positive requests use the compositor-observed client extent:

```bash
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~VulkanBackendRunsOneHundredResizeMinimizeRestoreOperationsOnLinux" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/" -m:1 -nr:false
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Runtime.Tests/TerraFX.Runtime.Tests.csproj" --configuration Release --filter "FullyQualifiedName~VulkanBackendRunsOneHundredResizeMinimizeRestoreOperationsOnLinux" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/" -m:1 -nr:false
```
