// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using TerraFX.Interop.Windows;

namespace TerraFX.Platform;

[GeneratedComInterface]
[Guid("28888FE3-C2A0-483A-A3EA-8CB1CE51FF3D")]
[SuppressMessage(
    "Design",
    "TFX0002:Order type members by category, staticness, accessibility, and name using ordinal comparison",
    Justification = "The method order is the native ITextStoreACP vtable contract.")]
internal unsafe partial interface IWindowsTextStoreACP
{
    [PreserveSig]
    int AdviseSink(Guid* riid, nint punk, uint mask);

    [PreserveSig]
    int UnadviseSink(nint punk);

    [PreserveSig]
    int RequestLock(uint lockFlags, int* sessionResult);

    [PreserveSig]
    int GetStatus(TS_STATUS* status);

    [PreserveSig]
    int QueryInsert(int start, int end, uint textLength, int* insertStart, int* insertEnd);

    [PreserveSig]
    int GetSelection(uint index, uint count, TS_SELECTION_ACP* selection, uint* fetched);

    [PreserveSig]
    int SetSelection(uint count, TS_SELECTION_ACP* selection);

    [PreserveSig]
    int GetText(
        int start,
        int end,
        char* text,
        uint textCapacity,
        uint* textLength,
        TS_RUNINFO* runInfo,
        uint runInfoCapacity,
        uint* runInfoCount,
        int* next);

    [PreserveSig]
    int SetText(uint flags, int start, int end, char* text, uint textLength, TS_TEXTCHANGE* change);

    [PreserveSig]
    int GetFormattedText(int start, int end, nint* dataObject);

    [PreserveSig]
    int GetEmbedded(int position, Guid* service, Guid* iid, nint* unknown);

    [PreserveSig]
    int QueryInsertEmbedded(Guid* service, nint formatEtc, int* insertable);

    [PreserveSig]
    int InsertEmbedded(uint flags, int start, int end, nint dataObject, TS_TEXTCHANGE* change);

    [PreserveSig]
    int InsertTextAtSelection(uint flags, char* text, uint textLength, int* start, int* end, TS_TEXTCHANGE* change);

    [PreserveSig]
    int InsertEmbeddedAtSelection(uint flags, nint dataObject, int* start, int* end, TS_TEXTCHANGE* change);

    [PreserveSig]
    int RequestSupportedAttrs(uint flags, uint count, Guid* attributes);

    [PreserveSig]
    int RequestAttrsAtPosition(int position, uint count, Guid* attributes, uint flags);

    [PreserveSig]
    int RequestAttrsTransitioningAtPosition(int position, uint count, Guid* attributes, uint flags);

    [PreserveSig]
    int FindNextAttrTransition(
        int start,
        int halt,
        uint count,
        Guid* attributes,
        uint flags,
        int* next,
        int* found,
        int* transition);

    [PreserveSig]
    int RetrieveRequestedAttrs(uint count, TS_ATTRVAL* attributes, uint* fetched);

    [PreserveSig]
    int GetEndACP(int* end);

    [PreserveSig]
    int GetActiveView(uint* view);

    [PreserveSig]
    int GetACPFromPoint(uint view, POINT* point, uint flags, int* acp);

    [PreserveSig]
    int GetTextExt(uint view, int start, int end, RECT* rect, int* clipped);

    [PreserveSig]
    int GetScreenExt(uint view, RECT* rect);

    [PreserveSig]
    int GetWnd(uint view, nint* window);
}
