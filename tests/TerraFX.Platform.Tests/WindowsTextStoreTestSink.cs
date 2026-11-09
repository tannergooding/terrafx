// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Runtime.InteropServices.Marshalling;
using TerraFX.Interop.Windows;

namespace TerraFX.Platform.Tests;

[GeneratedComClass]
internal sealed unsafe partial class WindowsTextStoreTestSink : IWindowsTextStoreTestSink
{
    internal Action? LockGranted { get; set; }

    internal int LockResult { get; set; }

    internal int ReadTextResult { get; set; }

    internal int SetTextResult { get; set; }

    internal string? SnapshotText { get; set; }

    internal TS_TEXTCHANGE TextChange { get; set; }

    public int OnAttrsChange(int start, int end, uint count, Guid* attributes)
    {
        _ = start;
        _ = end;
        _ = count;
        _ = attributes;
        return 0;
    }

    public int OnEndEditTransaction() => 0;

    public int OnLayoutChange(TsLayoutCode code, uint view)
    {
        _ = code;
        _ = view;
        return 0;
    }

    public int OnLockGranted(uint flags)
    {
        _ = flags;
        LockGranted?.Invoke();
        return LockResult;
    }

    public int OnSelectionChange() => 0;

    public int OnStartEditTransaction() => 0;

    public int OnStatusChange(uint flags)
    {
        _ = flags;
        return 0;
    }

    public int OnTextChange(uint flags, TS_TEXTCHANGE* change)
    {
        _ = flags;
        TextChange = (change is null) ? default : *change;
        return 0;
    }
}
