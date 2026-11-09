// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace TerraFX.Platform;

[GeneratedComInterface]
[Guid("5F20AA40-B57A-4F34-96AB-3576F377CC79")]
[SuppressMessage(
    "Design",
    "TFX0002:Order type members by category, staticness, accessibility, and name using ordinal comparison",
    Justification = "The method order is the native ITfContextOwnerCompositionSink vtable contract.")]
internal unsafe partial interface IWindowsTextStoreCompositionSink
{
    [PreserveSig]
    int OnStartComposition(nint compositionView, int* accepted);

    [PreserveSig]
    int OnUpdateComposition(nint compositionView, nint range);

    [PreserveSig]
    int OnEndComposition(nint compositionView);
}
