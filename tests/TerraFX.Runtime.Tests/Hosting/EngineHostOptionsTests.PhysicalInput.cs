// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
}
