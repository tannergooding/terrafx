# Platform test procedures

Use the SDK selected by `global.json`. Run project tests before platform-specific probes:

```powershell
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Debug
dotnet test .\tests\TerraFX.Platform.Tests\TerraFX.Platform.Tests.csproj --configuration Release
```

Runtime host/input routing tests are in `TerraFX.Runtime.Tests`.

## Windows interactive input

These opt-in tests open a visible window. Focus it and provide the requested input:

```powershell
$env:TERRAFX_RUN_M04_PHYSICAL_INPUT = '1'
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.ManualWindowsInteractivePhysicalKeyboardInputUsesHostLiveUiRoute"
Remove-Item Env:\TERRAFX_RUN_M04_PHYSICAL_INPUT

$env:TERRAFX_RUN_M04_PHYSICAL_TEXT = '1'
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Runtime.Tests.Hosting.EngineHostOptionsTests.ManualWindowsInteractivePhysicalTextInputUsesHostLiveUiSession"
Remove-Item Env:\TERRAFX_RUN_M04_PHYSICAL_TEXT
```

The text probe requires Windows TSF and an active IME. It is separate from the keyboard-routing test.

## Isolated X11

Use a test-owned Xvfb display with XTEST and `xkbcomp`; set `TERRAFX_TEST_ISOLATED_X11=1` only with that display (commonly `:99`). Never set it against WSLg `:0`; tests must not use the user's display or clipboard.

```bash
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug --filter "FullyQualifiedName~X11ClipboardTests|FullyQualifiedName~X11Only" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
DISPLAY=:99 TERRAFX_TEST_ISOLATED_X11=1 "$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release --filter "FullyQualifiedName~X11ClipboardTests|FullyQualifiedName~X11Only" "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

## WSLg and Wayland

Run Linux tests from outside the checkout so `global.json` selects the local Linux SDK. Keep outputs isolated from Windows:

```bash
export WORKTREE=/mnt/d/path/to/worktree
cd /tmp
export DOTNET_ROOT="$WORKTREE/artifacts/linux/dotnet"
export DOTNET_ROOT_X64="$DOTNET_ROOT"
export DOTNET_MULTILEVEL_LOOKUP=0
export LD_LIBRARY_PATH="$WORKTREE/artifacts/linux/native/extracted/usr/lib/x86_64-linux-gnu"
export VK_LAYER_PATH="$WORKTREE/artifacts/linux/native/extracted/usr/share/vulkan/explicit_layer.d"
export VK_INSTANCE_LAYERS=VK_LAYER_KHRONOS_validation
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Debug "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
"$DOTNET_ROOT/dotnet" test "$WORKTREE/tests/TerraFX.Platform.Tests/TerraFX.Platform.Tests.csproj" --configuration Release "-p:BaseArtifactsPath=$WORKTREE/artifacts/linux/"
```

Private Sway/Weston integration tests require their isolated compositor and must not use WSLg's shared display. WSLg has no physical evdev input devices; compositor-driven or synthetic events do not qualify physical input.
