// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;
using TerraFX.Hosting;
using TerraFX.Input;
using TerraFX.Numerics;
using TerraFX.Platform;
using TerraFX.Runtime.Tests.Scenes;

namespace TerraFX.Runtime.Tests.Hosting;

internal sealed partial class EngineHostOptionsTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The PlatformHostOwner is disposed on its creating STA thread in the enclosing finally block.")]
    public void ManualWindowsInteractivePhysicalGamepadLifecycleUsesGameInput()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The physical GameInput witness requires Windows 11 x64.");
            return;
        }

        if (!StringComparer.Ordinal.Equals(
            Environment.GetEnvironmentVariable("TERRAFX_RUN_M04_GAMEPAD"),
            "1"))
        {
            Assert.Ignore("Set TERRAFX_RUN_M04_GAMEPAD=1 to run the interactive physical GameInput witness.");
            return;
        }

        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            CreateAssetStoreOptions()) {
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        PlatformHostOwner? platformOwner = null;

        try
        {
            platformOwner = PlatformHostOwner.Create(options);

            if (platformOwner.Platform.GamepadStatus != PlatformServiceStatus.Available)
            {
                Assert.Ignore("The documented GameInput PC runtime is unavailable; physical gamepad capture is disabled.");
                return;
            }

            TestContext.Progress.WriteLine($"GameInput status: {platformOwner.Platform.GamepadStatus}");
            var window = platformOwner.CreateWindow(new WindowOptions("TerraFX M04 GameInput witness - initializing") {
                ClientSize = new Vector2(640.0f, 360.0f),
                Visible = true,
            });

            if (!WindowsTestNativeMethods.SetForegroundWindow(
                platformOwner.GetNativeWindowBinding(window).NativeWindowOrSurface))
            {
                TestContext.Progress.WriteLine("The witness window could not be activated; use Alt+Tab to focus it.");
            }

            var knownDevices = new System.Collections.Generic.HashSet<InputDeviceHandle>();
            var gamepad = default(InputDeviceHandle);
            var sawInitialReconciliation = false;
            var sawButtonDown = false;
            var sawButtonUp = false;
            GamepadButtonValue? pressedButton = null;
            var baseline = Stopwatch.StartNew();

            while (baseline.Elapsed < TimeSpan.FromSeconds(2))
            {
                foreach (var inputEvent in platformOwner.Pump().Events)
                {
                    if ((inputEvent.Kind == PhysicalInputEventKind.DeviceAdded)
                        && (inputEvent.Device is { IsValid: true } device))
                    {
                        _ = knownDevices.Add(device);
                    }

                    if ((inputEvent.Kind == PhysicalInputEventKind.StateReconciliation)
                        && (inputEvent.Device is { IsValid: true } reconciledDevice))
                    {
                        gamepad = reconciledDevice;
                        sawInitialReconciliation = true;
                        TestContext.Progress.WriteLine($"Gamepad state reconciled: {gamepad.OwnerId}/{gamepad.Index}@{gamepad.Generation}, controls={inputEvent.ReconciliationState.Length}");
                    }
                }

                Thread.Sleep(10);
            }

            var connectPrompt = gamepad.IsValid
                ? "Focus this window, then press and release A/B/X/Y (not Guide)"
                : "Connect a physical gamepad, focus this window, then press and release A/B/X/Y (not Guide)";
            _ = platformOwner.SetTitle(window, connectPrompt);
            TestContext.Progress.WriteLine(connectPrompt);
            var connectTimeout = Stopwatch.StartNew();

            while ((!gamepad.IsValid || !sawButtonUp)
                && (connectTimeout.Elapsed < TimeSpan.FromSeconds(60)))
            {
                foreach (var inputEvent in platformOwner.Pump().Events)
                {
                    if ((inputEvent.Kind == PhysicalInputEventKind.DeviceAdded)
                        && (inputEvent.Device is { IsValid: true } addedDevice)
                        && !knownDevices.Contains(addedDevice))
                    {
                        TestContext.WriteLine($"New device announcement: {addedDevice.OwnerId}/{addedDevice.Index}@{addedDevice.Generation}");
                    }

                    if ((inputEvent.Kind == PhysicalInputEventKind.StateReconciliation)
                        && (inputEvent.Device is { IsValid: true } reconciledDevice)
                        && !knownDevices.Contains(reconciledDevice))
                    {
                        gamepad = reconciledDevice;
                        sawInitialReconciliation = true;
                        TestContext.WriteLine($"Gamepad state reconciled: {gamepad.OwnerId}/{gamepad.Index}@{gamepad.Generation}, controls={inputEvent.ReconciliationState.Length}");
                    }
                    else if ((inputEvent.Kind is PhysicalInputEventKind.GamepadButtonDown or PhysicalInputEventKind.GamepadButtonUp)
                        && (inputEvent.Device is { IsValid: true } buttonDevice)
                        && !knownDevices.Contains(buttonDevice))
                    {
                        gamepad = buttonDevice;
                        TestContext.Progress.WriteLine($"Gamepad button event: {inputEvent.Kind}, device={gamepad.OwnerId}/{gamepad.Index}@{gamepad.Generation}, button={inputEvent.Value?.GamepadButton}");
                    }

                    if (!gamepad.IsValid || (inputEvent.Device != gamepad))
                    {
                        continue;
                    }

                    if ((inputEvent.Kind == PhysicalInputEventKind.GamepadButtonDown)
                        && (inputEvent.Value?.GamepadButton is { } buttonDown))
                    {
                        pressedButton = buttonDown;
                        sawButtonDown = true;
                    }
                    else if ((inputEvent.Kind == PhysicalInputEventKind.GamepadButtonUp)
                        && (inputEvent.Value?.GamepadButton is { } buttonUp)
                        && (pressedButton == buttonUp))
                    {
                        sawButtonUp = true;
                    }
                }

                if (!sawButtonUp)
                {
                    Thread.Sleep(10);
                }
            }

            Assert.Multiple(() => {
                Assert.That(gamepad.IsValid, Is.True, "No new physical GameInput gamepad was observed.");
                Assert.That(sawInitialReconciliation, Is.True, "The new device's initial state was not reconciled.");
                Assert.That(sawButtonDown, Is.True, "No physical GameInput button-down was observed.");
                Assert.That(sawButtonUp, Is.True, "The matching GameInput button-up was not observed.");
            });

            _ = platformOwner.SetTitle(window, "Disconnect the gamepad and leave it disconnected");
            TestContext.Progress.WriteLine($"Captured {gamepad}; disconnect the gamepad and leave it disconnected until removal is observed.");
            var removalTimeout = Stopwatch.StartNew();
            var sawRemoval = false;

            while (!sawRemoval
                && (removalTimeout.Elapsed < TimeSpan.FromSeconds(90)))
            {
                foreach (var inputEvent in platformOwner.Pump().Events)
                {
                    if ((inputEvent.Kind == PhysicalInputEventKind.DeviceRemoved)
                        && (inputEvent.Device == gamepad))
                    {
                        sawRemoval = true;
                        _ = platformOwner.SetTitle(window, "Removal observed; reconnect the same gamepad now");
                        TestContext.Progress.WriteLine("Gamepad removal observed; reconnect the same gamepad now.");
                    }
                }

                if (!sawRemoval)
                {
                    Thread.Sleep(10);
                }
            }

            Assert.That(sawRemoval, Is.True, "No device removal was observed for the connected gamepad.");
            var reconnectedGamepad = default(InputDeviceHandle);
            var reconnectTimeout = Stopwatch.StartNew();

            while (!reconnectedGamepad.IsValid
                && (reconnectTimeout.Elapsed < TimeSpan.FromSeconds(90)))
            {
                foreach (var inputEvent in platformOwner.Pump().Events)
                {
                    if ((inputEvent.Kind == PhysicalInputEventKind.StateReconciliation)
                        && (inputEvent.Device is { IsValid: true } reconnectedDevice)
                        && (reconnectedDevice != gamepad))
                    {
                        reconnectedGamepad = reconnectedDevice;
                        TestContext.Progress.WriteLine($"Reconnected gamepad state observed: {reconnectedGamepad.OwnerId}/{reconnectedGamepad.Index}@{reconnectedGamepad.Generation}");
                    }
                }

                if (!reconnectedGamepad.IsValid)
                {
                    Thread.Sleep(10);
                }
            }

            Assert.Multiple(() => {
                Assert.That(reconnectedGamepad.IsValid, Is.True, "No reconnected gamepad generation was observed.");
                Assert.That(reconnectedGamepad, Is.Not.EqualTo(gamepad));
                Assert.That(reconnectedGamepad.OwnerId, Is.EqualTo(gamepad.OwnerId));

                if (reconnectedGamepad.Index == gamepad.Index)
                {
                    Assert.That(reconnectedGamepad.Generation, Is.GreaterThan(gamepad.Generation));
                }
            });
        }
        finally
        {
            platformOwner?.DisposeFromOwner();
        }
    }

    [Test]
    public async Task ManualWindowsInteractivePhysicalKeyboardInputUsesHostLiveUiRoute()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The physical keyboard witness requires Windows 11 x64.");
            return;
        }

        if (!StringComparer.Ordinal.Equals(
            Environment.GetEnvironmentVariable("TERRAFX_RUN_M04_PHYSICAL_INPUT"),
            "1"))
        {
            Assert.Ignore("Set TERRAFX_RUN_M04_PHYSICAL_INPUT=1 to run the interactive physical keyboard witness.");
            return;
        }

        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var assetStoreOptions = new AssetStoreOptions(
            SceneAssetStoreTests.Package.WritePackage(packageBytes),
            SHA256.HashData(packageBytes),
            CreateAssetStoreOptions().Registration);
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            assetStoreOptions) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        EngineHost? host = null;
        InputRouter? router = null;
        InputRecorder? recorder = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 adapter satisfies Interactive hosting: {exception.Message}");
                return;
            }

            var runningHost = host
                ?? throw new InvalidOperationException("Interactive EngineHost startup returned no host.");
            var windowReceipt = await runningHost.CreateWindowAsync(new WindowOptions(
                "TerraFX M04 physical keyboard witness - press and release A") {
                ClientSize = new Vector2(640.0f, 360.0f),
                Visible = true,
            }).ConfigureAwait(false);
            Assert.That(windowReceipt.Status, Is.EqualTo(WindowCreationStatus.Created));
            var window = windowReceipt.Window
                ?? throw new InvalidOperationException("Interactive window creation returned no WindowHandle.");
            var actionId = Guid.NewGuid();
            var map = new InputActionMapV1(
                schemaVersion: 1,
                mapId: Guid.NewGuid(),
                revision: 1,
                initialActiveContextMask: 1,
                actions: [new ActionDefinition(actionId, "Physical A", InputActionKind.Digital)],
                contexts: [new ContextDefinition(Guid.NewGuid(), bit: 0)],
                bindings: [
                    new BindingDefinition(Guid.NewGuid(), actionId, contextMask: 1, BindingKind.Button) {
                        Control = new DigitalControl(new KeyboardControl(usagePage: 0x07, usage: 0x04)),
                    },
                ]);

            await runningHost.InvokeAsync(context => {
                var hostRouter = context.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
                router = hostRouter;
                Assert.That(hostRouter.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
                _ = hostRouter.ServiceInputControlBoundary();
                recorder = hostRouter.StartRecording();
            }).ConfigureAwait(false);

            TestContext.WriteLine("Focus the visible witness window, then press and hold A briefly before releasing it.");
            var timeout = Stopwatch.StartNew();
            var sawKeyDown = false;
            var sawKeyUp = false;

            while (!sawKeyUp && (timeout.Elapsed < TimeSpan.FromSeconds(90)))
            {
                var action = router!.GetHostActionSnapshot().Actions.Single();
                sawKeyDown |= action.IsDownAtEnd || action.PressedThisTick;
                sawKeyUp |= sawKeyDown && (action.ReleasedThisTick || !action.IsDownAtEnd);
                await Task.Delay(10).ConfigureAwait(false);
            }

            Assert.That(sawKeyDown, Is.True, "No physical A key-down reached the HostLiveUI recipient.");
            Assert.That(sawKeyUp, Is.True, "The physical A key-up was not observed after the key-down.");

            InputRecording? recording = null;
            await runningHost.InvokeAsync(context => {
                _ = router!.ServiceInputControlBoundary();
                recording = recorder!.Complete();
                _ = context.DisposeInputRouter(router!);
            }).ConfigureAwait(false);

            var keyEvents = recording!.Entries
                .Where(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput)
                .Select(static entry => entry.PhysicalEvent)
                .OfType<PhysicalInputEvent>()
                .Where(static inputEvent => inputEvent.Value?.DigitalControl?.Keyboard is {
                    Kind: KeyboardControlKind.KeyboardHid,
                    UsagePage: 0x07,
                    Usage: 0x04,
                })
                .ToArray();

            Assert.Multiple(() => {
                Assert.That(keyEvents.Any(static inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyDown), Is.True);
                Assert.That(keyEvents.Any(static inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyUp), Is.True);
                Assert.That(keyEvents.All(static inputEvent => inputEvent.Device is { IsValid: true }), Is.True);
                Assert.That(keyEvents.All(inputEvent => inputEvent.Window == window), Is.True);
            });
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    [Test]
    public async Task ManualWindowsInteractivePhysicalTextInputUsesHostLiveUiSession()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            || (RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            Assert.Ignore("The physical text-input witness requires Windows 11 x64.");
            return;
        }

        if (!StringComparer.Ordinal.Equals(
            Environment.GetEnvironmentVariable("TERRAFX_RUN_M04_PHYSICAL_TEXT"),
            "1"))
        {
            Assert.Ignore("Set TERRAFX_RUN_M04_PHYSICAL_TEXT=1 to run the interactive physical text-input witness.");
            return;
        }

        var packageBytes = SceneAssetStoreTests.Package.CreatePackage();
        var assetStoreOptions = new AssetStoreOptions(
            SceneAssetStoreTests.Package.WritePackage(packageBytes),
            SHA256.HashData(packageBytes),
            CreateAssetStoreOptions().Registration);
        var options = new EngineHostOptions(
            HostProfile.Interactive,
            PlatformBackend.WindowsNative,
            GraphicsBackend.D3D12,
            D3D12GraphicsBackend.CreateRegistration(),
            assetStoreOptions) {
            CpuWorkerCount = 0,
            GraphicsFrameSlotCount = 2,
            PersistenceMetadata = CreatePersistenceMetadata(),
        };
        EngineHost? host = null;
        InputRouter? router = null;
        InputRecorder? recorder = null;
        TextInputSession? textSession = null;
        var document = new StringBuilder();
        var documentRevision = 1L;
        var sawCompositionStart = false;
        var sawCompositionUpdate = false;
        var sawCompositionEnd = false;
        var sawCompositionCancel = false;
        var sawTextCommit = false;
        TimeSpan? commitObservedAt = null;
        string? lastDiagnosticCode = null;

        try
        {
            try
            {
                host = await EngineHost.StartAsync(options).ConfigureAwait(false);
            }
            catch (NotSupportedException exception)
            {
                Assert.Ignore($"No D3D12 adapter satisfies Interactive hosting: {exception.Message}");
                return;
            }

            var runningHost = host
                ?? throw new InvalidOperationException("Interactive EngineHost startup returned no host.");
            var windowReceipt = await runningHost.CreateWindowAsync(new WindowOptions(
                "TerraFX M04 TSF witness - compose and commit with an active IME") {
                ClientSize = new Vector2(640.0f, 360.0f),
                Visible = true,
            }).ConfigureAwait(false);
            Assert.That(windowReceipt.Status, Is.EqualTo(WindowCreationStatus.Created));
            var window = windowReceipt.Window
                ?? throw new InvalidOperationException("Interactive window creation returned no WindowHandle.");
            var actionId = Guid.NewGuid();
            var map = new InputActionMapV1(
                schemaVersion: 1,
                mapId: Guid.NewGuid(),
                revision: 1,
                initialActiveContextMask: 1,
                actions: [new ActionDefinition(actionId, "Physical H", InputActionKind.Digital)],
                contexts: [new ContextDefinition(Guid.NewGuid(), bit: 0)],
                bindings: [
                    new BindingDefinition(Guid.NewGuid(), actionId, contextMask: 1, BindingKind.Button) {
                        Control = new DigitalControl(new KeyboardControl(usagePage: 0x07, usage: 0x0B)),
                    },
                ]);

            await runningHost.InvokeAsync(context => {
                var hostRouter = context.CreateInputRouter(InputRecipientKind.HostLiveUI, window);
                router = hostRouter;
                Assert.That(hostRouter.RegisterMap(map).Status, Is.EqualTo(InputMapStatus.Registered));
                _ = hostRouter.ServiceInputControlBoundary();
                recorder = hostRouter.StartRecording(recordTextBatches: true);
                textSession = hostRouter.BeginTextSession(new TextInputSessionOptions(
                    window,
                    TextInputPurpose.FreeForm,
                    documentRevision,
                    surroundingText: string.Empty,
                    selectionStart: 0,
                    selectionLength: 0,
                    caretPosition: new Vector2(8.0f, 8.0f),
                    caretSize: new Vector2(1.0f, 18.0f)));
            }).ConfigureAwait(false);

            TestContext.WriteLine("Focus the visible window, compose a short string with an active IME, then commit it.");
            var timeout = Stopwatch.StartNew();

            while ((!sawTextCommit || ((timeout.Elapsed - commitObservedAt.GetValueOrDefault()) < TimeSpan.FromMilliseconds(500)))
                && (timeout.Elapsed < TimeSpan.FromSeconds(90)))
            {
                await runningHost.InvokeAsync(context => {
                    _ = context;
                    var activeSession = textSession
                        ?? throw new InvalidOperationException("The live text-input witness has no active session.");

                    while (activeSession.TryReadBatch(out var batch))
                    {
                        var textBatch = batch
                            ?? throw new InvalidOperationException("The live text-input witness returned a null batch.");

                        foreach (var change in textBatch.Changes)
                        {
                            switch (change.Kind)
                            {
                                case TextInputChangeKind.CompositionStarted:
                                {
                                    sawCompositionStart = true;
                                    break;
                                }

                                case TextInputChangeKind.CompositionUpdated:
                                {
                                    sawCompositionUpdate = true;
                                    break;
                                }

                                case TextInputChangeKind.CompositionEnded:
                                {
                                    sawCompositionEnd = true;
                                    break;
                                }

                                case TextInputChangeKind.CompositionCanceled:
                                {
                                    sawCompositionCancel = true;
                                    break;
                                }

                                case TextInputChangeKind.TextEditRequested:
                                {
                                    var editStart = change.EditStart
                                        ?? throw new InvalidOperationException("A text edit omitted its start.");
                                    var deleteLength = change.DeleteLength
                                        ?? throw new InvalidOperationException("A text edit omitted its delete length.");
                                    _ = document.Remove(editStart, deleteLength);
                                    break;
                                }

                                case TextInputChangeKind.TextCommitted:
                                {
                                    var editStart = change.EditStart ?? document.Length;
                                    var deleteLength = change.DeleteLength ?? 0;
                                    _ = document.Remove(editStart, deleteLength);
                                    _ = document.Insert(editStart, change.Text
                                        ?? throw new InvalidOperationException("A committed text change omitted its text."));
                                    sawTextCommit = true;
                                    commitObservedAt ??= timeout.Elapsed;
                                    break;
                                }
                            }
                        }

                        if (textBatch.ResultDocumentRevision > documentRevision)
                        {
                            documentRevision = textBatch.ResultDocumentRevision;
                            activeSession.Update(new TextInputSessionOptions(
                                window,
                                TextInputPurpose.FreeForm,
                                documentRevision,
                                document.ToString(),
                                selectionStart: document.Length,
                                selectionLength: 0,
                                caretPosition: new Vector2(8.0f, 8.0f),
                                caretSize: new Vector2(1.0f, 18.0f)));
                        }
                    }

                    lastDiagnosticCode = activeSession.LastDiagnostic?.Code;
                }).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                if (!sawTextCommit)
                {
                    await Task.Delay(20).ConfigureAwait(false);
                }
            }

            InputRecording? recording = null;
            await runningHost.InvokeAsync(context => {
                recording = recorder!.Complete();
                _ = context.DisposeInputRouter(router!);
            }).ConfigureAwait(false);

            var physicalKeyEvents = recording!.Entries
                .Where(static entry => entry.Kind == InputRecordingEntryKind.PhysicalInput)
                .Select(static entry => entry.PhysicalEvent)
                .OfType<PhysicalInputEvent>()
                .Where(static inputEvent => inputEvent.Value?.DigitalControl?.Keyboard is {
                    Kind: KeyboardControlKind.KeyboardHid,
                    UsagePage: 0x07,
                    Usage: 0x0B,
                })
                .ToArray();
            TestContext.WriteLine(
                $"Committed UTF-16 length={document.Length}; physical-H-down="
                + $"{physicalKeyEvents.Any(static inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyDown)}; "
                + $"physical-H-up={physicalKeyEvents.Any(static inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyUp)}; "
                + $"composition-start={sawCompositionStart}; "
                + $"composition-update={sawCompositionUpdate}; composition-end={sawCompositionEnd}; "
                + $"composition-cancel={sawCompositionCancel}; diagnostic={lastDiagnosticCode ?? "none"}");
            Assert.Multiple(() => {
                Assert.That(physicalKeyEvents.Any(static inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyDown), Is.True);
                Assert.That(physicalKeyEvents.Any(static inputEvent => inputEvent.Kind == PhysicalInputEventKind.KeyUp), Is.True);
                Assert.That(physicalKeyEvents.All(static inputEvent => inputEvent.Device is { IsValid: true }), Is.True);
                Assert.That(physicalKeyEvents.All(inputEvent => inputEvent.Window == window), Is.True);
                Assert.That(sawTextCommit, Is.True, "No physical text commit reached the focused HostLiveUI text session.");
                Assert.That(document.Length, Is.GreaterThan(0));
                Assert.That(sawCompositionStart, Is.True, "No TSF composition-start event was captured.");
                Assert.That(sawCompositionUpdate, Is.True, "No TSF preedit update was captured.");
                Assert.That(sawCompositionEnd, Is.True, "No TSF composition-end event was captured.");
            });
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
