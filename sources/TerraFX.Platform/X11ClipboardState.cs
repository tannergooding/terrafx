// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TerraFX;

namespace TerraFX.Platform;

internal sealed class X11ClipboardState
{
    internal const int MaximumPendingReads = 64;
    internal const int TransferChunkBytes = 16 * 1024;
    internal const long TransferTimeoutMilliseconds = 5000;

    internal X11ClipboardState(
        nuint selectionAtom,
        nuint targetsAtom,
        nuint utf8StringAtom,
        nuint textPlainUtf8Atom,
        nuint incrAtom)
    {
        ArgumentOutOfRangeException.ThrowIfZero(selectionAtom);
        ArgumentOutOfRangeException.ThrowIfZero(targetsAtom);
        ArgumentOutOfRangeException.ThrowIfZero(utf8StringAtom);
        ArgumentOutOfRangeException.ThrowIfZero(textPlainUtf8Atom);
        ArgumentOutOfRangeException.ThrowIfZero(incrAtom);
        SelectionAtom = selectionAtom;
        TargetsAtom = targetsAtom;
        Utf8StringAtom = utf8StringAtom;
        TextPlainUtf8Atom = textPlainUtf8Atom;
        IncrAtom = incrAtom;
    }

    internal nuint IncrAtom { get; }

    internal uint NextPropertyId { get; set; }

    internal byte[]? OwnedTextBytes { get; set; }

    internal nuint OwnedWindow { get; set; }

    internal Dictionary<nuint, ReadTransfer> PendingReads { get; } = [];

    internal Dictionary<(nuint Requestor, nuint Property), WriteTransfer> PendingWrites { get; } = [];

    internal nuint SelectionAtom { get; }

    internal nuint TargetsAtom { get; }

    internal nuint TextPlainUtf8Atom { get; }

    internal nuint Utf8StringAtom { get; }

    internal sealed class ReadTransfer
    {
        internal ReadTransfer(
            ClipboardReadRequest request,
            nuint requestor,
            nuint property,
            nuint selectionOwner,
            nuint target,
            Action<ClipboardReadResponse> complete,
            long startTimestamp,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(complete);
            Request = request;
            Requestor = requestor;
            Property = property;
            SelectionOwner = selectionOwner;
            Target = target;
            Complete = complete;
            CancellationToken = cancellationToken;
            StartTimestamp = startTimestamp;
        }

        internal CancellationToken CancellationToken { get; }

        internal Action<ClipboardReadResponse> Complete { get; }

        internal bool IsIncremental { get; set; }

        internal MemoryStream Payload { get; } = new();

        internal nuint Property { get; }

        internal ClipboardReadRequest Request { get; }

        internal nuint Requestor { get; }

        internal bool ResponseCompleted { get; set; }

        internal nuint SelectionOwner { get; }

        internal long StartTimestamp { get; }

        internal nuint Target { get; set; }
    }

    internal sealed class WriteTransfer(nuint ownerWindow, nuint target, byte[] payload, long startTimestamp)
    {
        internal int Offset { get; set; }

        internal nuint OwnerWindow { get; } = ownerWindow;

        internal byte[] Payload { get; } = payload;

        internal long StartTimestamp { get; } = startTimestamp;

        internal nuint Target { get; } = target;
    }
}
