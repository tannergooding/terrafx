// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using TerraFX.Interop.Windows;

namespace TerraFX.Platform.Tests;

[GeneratedComInterface]
[Guid("22D44C94-A419-4542-A272-AE26093ECECF")]
[SuppressMessage(
    "Design",
    "TFX0002:Order type members by category, staticness, accessibility, and name using ordinal comparison",
    Justification = "The method order is the native ITextStoreACPSink vtable contract.")]
internal unsafe partial interface IWindowsTextStoreTestSink
{
    [PreserveSig]
    int OnTextChange(uint flags, TS_TEXTCHANGE* change);

    [PreserveSig]
    int OnSelectionChange();

    [PreserveSig]
    int OnLayoutChange(TsLayoutCode code, uint view);

    [PreserveSig]
    int OnStatusChange(uint flags);

    [PreserveSig]
    int OnAttrsChange(int start, int end, uint count, Guid* attributes);

    [PreserveSig]
    int OnLockGranted(uint flags);

    [PreserveSig]
    int OnStartEditTransaction();

    [PreserveSig]
    int OnEndEditTransaction();
}
