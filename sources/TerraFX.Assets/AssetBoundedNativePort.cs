// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace TerraFX.Assets;

internal sealed unsafe partial class AssetBoundedNativePort
{
    private const int LinuxEintr = 4;
    private const int LinuxLockShared = 1;
    private const int LinuxOpenCloseOnExec = 0x80000;
    private const int LinuxOpenNonBlocking = 0x800;
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxRegularFile = 0x8000;
    private const int LinuxStatTypeMask = 0xF000;
    private const int MaximumLinuxPathBytes = 4095;
    private const long MaximumPackageBytes = 8L * 1024 * 1024 * 1024;
    private const int MinimumPackageBytes = 64;
    private const int NativeScratchBytes = 65_536;
    private const uint WindowsFileFlagOverlapped = 0x40000000;
    private const uint WindowsFileFlagRandomAccess = 0x10000000;
    private const int WindowsFileTypeDisk = 1;
    private const uint WindowsGenericRead = 0x80000000;
    private const uint WindowsIoIncomplete = 996;
    private const uint WindowsIoPending = 997;
    private const uint WindowsOpenExisting = 3;
    private const uint WindowsShareRead = 1;

    private int _activeOperation;
    private bool _controlsReleaseAttempted;
    private bool _initializationAttempted;
    private AssetBoundedFailureFact _initializationFailure;
    private nint _ioEvent;
    private AssetBoundedFailureFact _ioEventCloseFailure;
    private bool _isInitialized;
    private int _openFileCount;
    private AssetBoundedWindowsOverlapped _overlapped;
    private readonly int _ownerThreadId;
    private nint _scratch;

    internal AssetBoundedNativePort(int ownerThreadId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ownerThreadId);
        _ownerThreadId = ownerThreadId;
    }

    internal bool InitializationAttempted => _initializationAttempted;

    private bool IsOwnerThread => Environment.CurrentManagedThreadId == _ownerThreadId;

    private static AssetBoundedFailureFact CloseFileCore(
        AssetBoundedNativeFile file,
        ref int openFileCount)
    {
        if (file.CloseAttempted)
        {
            return file.CloseFailure;
        }

        if (!file.IsOpen)
        {
            return default;
        }

        file.CloseAttempted = true;

        if (OperatingSystem.IsWindows())
        {
            if (WindowsNative.CloseHandle(file.WindowsHandle) != 0)
            {
                file.WindowsHandle = -1;
                file.Disposition = AssetBoundedFileDisposition.Consumed;
                var releaseFailure = ReleaseFileUse(file, ref openFileCount);
                file.CloseFailure = releaseFailure;
                return releaseFailure;
            }

            var error = Marshal.GetLastSystemError();
            file.Disposition = AssetBoundedFileDisposition.Indeterminate;
            file.CloseFailure = CreateFailure(
                AssetBoundedFailureStage.CandidateFileRelease,
                AssetBoundedFailureKind.Native,
                AssetBoundedNativeApi.WindowsCloseHandle,
                error,
                file.Disposition);
            return file.CloseFailure;
        }

        var closeResult = LinuxNative.Close(file.LinuxDescriptor);

        if (closeResult == 0)
        {
            file.LinuxDescriptor = -1;
            file.Disposition = AssetBoundedFileDisposition.Consumed;
            var releaseFailure = ReleaseFileUse(file, ref openFileCount);
            file.CloseFailure = releaseFailure;
            return releaseFailure;
        }

        var closeError = Marshal.GetLastSystemError();

        if (closeError == LinuxEintr)
        {
            file.LinuxDescriptor = -1;
            file.Disposition = AssetBoundedFileDisposition.ConsumedWithError;
        }
        else
        {
            file.Disposition = AssetBoundedFileDisposition.Indeterminate;
        }

        file.CloseFailure = CreateFailure(
            AssetBoundedFailureStage.CandidateFileRelease,
            AssetBoundedFailureKind.Native,
            AssetBoundedNativeApi.LinuxClose,
            closeError,
            file.Disposition);

        if (file.Disposition is AssetBoundedFileDisposition.ConsumedWithError)
        {
            var releaseFailure = ReleaseFileUse(file, ref openFileCount);

            if (releaseFailure.Kind is not AssetBoundedFailureKind.None)
            {
                return releaseFailure;
            }
        }

        return file.CloseFailure;
    }

    private static AssetBoundedFailureFact CreateFailure(
        AssetBoundedFailureStage stage,
        AssetBoundedFailureKind kind,
        AssetBoundedNativeApi nativeApi,
        int nativeError,
        AssetBoundedFileDisposition disposition)
        => new(stage, kind, nativeApi, nativeError, Guid.Empty, 0, disposition);

    private static bool IsEmpty(in AssetBoundedNativeFile file)
        => (file.Api is AssetBoundedNativeApi.None)
            && !file.CloseAttempted
            && (file.Port is null)
            && !file.PortUseCounted
            && (file.Disposition is AssetBoundedFileDisposition.NotAttempted);

    private static bool IsSupportedTarget()
        => OperatingSystem.IsWindows()
            || (OperatingSystem.IsLinux() && (RuntimeInformation.ProcessArchitecture is Architecture.X64));

    private static AssetBoundedFailureFact ReleaseFileUse(
        AssetBoundedNativeFile file,
        ref int openFileCount)
    {
        if (!file.PortUseCounted)
        {
            return default;
        }

        file.PortUseCounted = false;

        if (openFileCount <= 0)
        {
            return CreateFailure(
                AssetBoundedFailureStage.CandidateFileRelease,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedNativeApi.None,
                0,
                file.Disposition);
        }

        openFileCount--;
        return default;
    }

    internal AssetBoundedFailureFact CloseFile(AssetBoundedNativeFile file)
    {
        if (file.CloseAttempted)
        {
            return file.CloseFailure;
        }

        if (!ReferenceEquals(file.Port, this))
        {
            return CreateFailure(
                AssetBoundedFailureStage.DisplacedOrFinalFileRelease,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedNativeApi.None,
                0,
                file.Disposition);
        }

        var refusal = TryEnter();

        if (refusal is not AssetBoundedRefusal.None)
        {
            return CreateFailure(
                AssetBoundedFailureStage.DisplacedOrFinalFileRelease,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedNativeApi.None,
                0,
                AssetBoundedFileDisposition.Indeterminate);
        }

        try
        {
            return CloseFileCore(file, ref _openFileCount);
        }
        finally
        {
            Exit();
        }
    }

    [SuppressMessage(
        "Usage",
        "CA1031:Do not catch general exception types",
        Justification = "The bounded lane maps closed-program hash failures into scalar facts after helper unwind.")]
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The preclaimed hash-provider owner is disposed in finally and its cleanup fact is retained separately.")]
    internal AssetBoundedFailureFact ComputeSha256(
        AssetBoundedNativeFile file,
        scoped Span<byte> destination,
        out AssetBoundedFailureFact hashProviderRelease)
    {
        hashProviderRelease = default;

        if (destination.Length != SHA256.HashSizeInBytes)
        {
            return CreateFailure(
                AssetBoundedFailureStage.Hash,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedNativeApi.None,
                0,
                file.Disposition);
        }

        var refusal = TryEnter();

        if (refusal is not AssetBoundedRefusal.None)
        {
            return CreateFailure(
                AssetBoundedFailureStage.Hash,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedNativeApi.None,
                0,
                file.Disposition);
        }

        IncrementalHash? hash = null;
        var primary = default(AssetBoundedFailureFact);

        try
        {
            if (!_isInitialized
                || _controlsReleaseAttempted
                || !ReferenceEquals(file.Port, this)
                || !file.IsOpen)
            {
                return CreateFailure(
                    AssetBoundedFailureStage.Hash,
                    AssetBoundedFailureKind.Invariant,
                    AssetBoundedNativeApi.None,
                    0,
                    file.Disposition);
            }

            hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long offset = 0;

            while (offset < file.Length)
            {
                var requested = checked((int)Math.Min((long)NativeScratchBytes, file.Length - offset));
                var chunkOffset = checked((ulong)offset);
                var chunkRead = 0;

                while (chunkRead < requested)
                {
                    var readFailure = ReadOneCore(
                        file,
                        checked(chunkOffset + checked((ulong)chunkRead)),
                        requested - chunkRead,
                        AssetBoundedFailureStage.Hash,
                        out var bytesRead);

                    if (readFailure.Kind is not AssetBoundedFailureKind.None)
                    {
                        primary = readFailure;
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        primary = CreateFailure(
                            AssetBoundedFailureStage.Hash,
                            AssetBoundedFailureKind.Layout,
                            file.Api,
                            0,
                            file.Disposition);
                        break;
                    }

                    hash.AppendData(new ReadOnlySpan<byte>((void*)_scratch, bytesRead));
                    chunkRead = checked(chunkRead + bytesRead);
                }

                if (primary.Kind is not AssetBoundedFailureKind.None)
                {
                    break;
                }

                offset = checked(offset + chunkRead);
            }

            if (primary.Kind is AssetBoundedFailureKind.None
                && (!hash.TryGetHashAndReset(destination, out var bytesWritten)
                    || (bytesWritten != destination.Length)))
            {
                primary = CreateFailure(
                    AssetBoundedFailureStage.Hash,
                    AssetBoundedFailureKind.Invariant,
                    AssetBoundedNativeApi.None,
                    0,
                    file.Disposition);
            }
        }
        catch (Exception)
        {
            primary = CreateFailure(
                AssetBoundedFailureStage.Hash,
                AssetBoundedFailureKind.ManagedExecution,
                AssetBoundedNativeApi.None,
                0,
                file.Disposition);
        }
        finally
        {
            var hashToDispose = hash;
            hash = null;

            if (hashToDispose is not null)
            {
                try
                {
                    hashToDispose.Dispose();
                }
                catch (Exception)
                {
                    hashProviderRelease = CreateFailure(
                        AssetBoundedFailureStage.HashProviderRelease,
                        AssetBoundedFailureKind.ManagedExecution,
                        AssetBoundedNativeApi.None,
                        0,
                        file.Disposition);
                }
            }

            Exit();
        }

        return primary;
    }

    internal AssetBoundedFailureFact Initialize()
    {
        var refusal = TryEnter();

        if (refusal is not AssetBoundedRefusal.None)
        {
            return CreateFailure(
                AssetBoundedFailureStage.Bootstrap,
                AssetBoundedFailureKind.Profile,
                AssetBoundedNativeApi.None,
                0,
                AssetBoundedFileDisposition.NotAttempted);
        }

        try
        {
            if (_controlsReleaseAttempted)
            {
                return CreateFailure(
                    AssetBoundedFailureStage.Bootstrap,
                    AssetBoundedFailureKind.Profile,
                    AssetBoundedNativeApi.None,
                    0,
                    AssetBoundedFileDisposition.NotAttempted);
            }

            if (_initializationAttempted)
            {
                return _initializationFailure;
            }

            _initializationAttempted = true;

            if (!IsSupportedTarget())
            {
                _initializationFailure = CreateFailure(
                    AssetBoundedFailureStage.Bootstrap,
                    AssetBoundedFailureKind.Profile,
                    AssetBoundedNativeApi.None,
                    0,
                    AssetBoundedFileDisposition.NotAttempted);
                return _initializationFailure;
            }

            _scratch = (nint)NativeMemory.Alloc((nuint)NativeScratchBytes);

            if (_scratch == 0)
            {
                _initializationFailure = CreateFailure(
                    AssetBoundedFailureStage.Bootstrap,
                    AssetBoundedFailureKind.ManagedExecution,
                    AssetBoundedNativeApi.None,
                    0,
                    AssetBoundedFileDisposition.NotAttempted);
                return _initializationFailure;
            }

            if (OperatingSystem.IsWindows())
            {
                _ioEvent = WindowsNative.CreateEventW(
                    securityAttributes: 0,
                    manualReset: 1,
                    initialState: 0,
                    name: 0);

                if (_ioEvent == 0)
                {
                    var error = Marshal.GetLastSystemError();
                    NativeMemory.Free((void*)_scratch);
                    _scratch = 0;
                    _initializationFailure = CreateFailure(
                        AssetBoundedFailureStage.Open,
                        AssetBoundedFailureKind.Native,
                        AssetBoundedNativeApi.WindowsCreateEvent,
                        error,
                        AssetBoundedFileDisposition.NotAttempted);
                    return _initializationFailure;
                }

                _overlapped = default;
                _overlapped.EventHandle = _ioEvent;
            }

            _isInitialized = true;
            return default;
        }
        finally
        {
            Exit();
        }
    }

    internal AssetBoundedRefusal OpenFile(
        string path,
        AssetBoundedNativeFile file,
        out AssetBoundedFailureFact primary,
        out AssetBoundedFailureFact fileRelease)
    {
        primary = default;
        fileRelease = default;

        if (string.IsNullOrEmpty(path) || path.Contains('\0', StringComparison.Ordinal))
        {
            return AssetBoundedRefusal.InvalidOptions;
        }

        var refusal = TryEnter();

        if (refusal is not AssetBoundedRefusal.None)
        {
            return refusal;
        }

        try
        {
            if (!_isInitialized || _controlsReleaseAttempted)
            {
                return AssetBoundedRefusal.ProfileUnavailable;
            }

            if (!AssetStore.IsSupportedBoundedPackagePath(path))
            {
                return AssetBoundedRefusal.InvalidOptions;
            }

            if (!IsEmpty(file))
            {
                return AssetBoundedRefusal.Busy;
            }

            if (_openFileCount == int.MaxValue)
            {
                return AssetBoundedRefusal.Capacity;
            }

            file.Port = this;

            if (OperatingSystem.IsWindows())
            {
                return OpenWindows(path, file, out primary, out fileRelease);
            }

            if (OperatingSystem.IsLinux()
                && (RuntimeInformation.ProcessArchitecture is Architecture.X64))
            {
                return OpenLinux(path, file, out primary, out fileRelease);
            }

            return AssetBoundedRefusal.ProfileUnavailable;
        }
        finally
        {
            Exit();
        }
    }

    internal AssetBoundedFailureFact ReadExactly(
        AssetBoundedNativeFile file,
        ulong offset,
        scoped Span<byte> destination,
        AssetBoundedFailureStage failureStage = AssetBoundedFailureStage.PayloadRead)
    {
        if (destination.IsEmpty)
        {
            return default;
        }

        var refusal = TryEnter();

        if (refusal is not AssetBoundedRefusal.None)
        {
            return CreateFailure(
                failureStage,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedNativeApi.None,
                0,
                file.Disposition);
        }

        try
        {
            if (!_isInitialized
                || !file.IsOpen
                || !ReferenceEquals(file.Port, this)
                || (offset > checked((ulong)file.Length))
                || (checked((ulong)destination.Length) > (checked((ulong)file.Length) - offset)))
            {
                return CreateFailure(
                    failureStage,
                    AssetBoundedFailureKind.Layout,
                    file.Api,
                    0,
                    file.Disposition);
            }

            return ReadExactlyCore(file, offset, destination, failureStage);
        }
        finally
        {
            Exit();
        }
    }

    internal AssetBoundedFailureFact ReleaseControls()
    {
        var refusal = TryEnter();

        if (refusal is not AssetBoundedRefusal.None)
        {
            return CreateFailure(
                AssetBoundedFailureStage.IoEventRelease,
                AssetBoundedFailureKind.Invariant,
                AssetBoundedNativeApi.None,
                0,
                AssetBoundedFileDisposition.NotAttempted);
        }

        try
        {
            if (_controlsReleaseAttempted)
            {
                return _ioEventCloseFailure;
            }

            if (_openFileCount != 0)
            {
                return CreateFailure(
                    AssetBoundedFailureStage.IoEventRelease,
                    AssetBoundedFailureKind.Invariant,
                    AssetBoundedNativeApi.None,
                    0,
                    AssetBoundedFileDisposition.NotFinal);
            }

            _controlsReleaseAttempted = true;

            if ((_ioEvent != 0) && OperatingSystem.IsWindows())
            {
                if (WindowsNative.CloseHandle(_ioEvent) == 0)
                {
                    var error = Marshal.GetLastSystemError();
                    _ioEventCloseFailure = CreateFailure(
                        AssetBoundedFailureStage.IoEventRelease,
                        AssetBoundedFailureKind.Native,
                        AssetBoundedNativeApi.WindowsCloseHandle,
                        error,
                        AssetBoundedFileDisposition.Indeterminate);
                }
                else
                {
                    _ioEvent = 0;
                    _overlapped = default;
                }
            }

            if (_scratch != 0)
            {
                NativeMemory.Free((void*)_scratch);
                _scratch = 0;
            }

            _isInitialized = false;
            return _ioEventCloseFailure;
        }
        finally
        {
            Exit();
        }
    }

    private void Exit()
        => Volatile.Write(ref _activeOperation, 0);

    private AssetBoundedRefusal OpenLinux(
        string path,
        AssetBoundedNativeFile file,
        out AssetBoundedFailureFact primary,
        out AssetBoundedFailureFact fileRelease)
    {
        primary = default;
        fileRelease = default;

        if ((path.Length > MaximumLinuxPathBytes) || (path[0] != '/'))
        {
            return AssetBoundedRefusal.InvalidOptions;
        }

        Span<byte> pathBytes = stackalloc byte[MaximumLinuxPathBytes + 1];

        for (var index = 0; index < path.Length; index++)
        {
            var value = path[index];

            if (value > 0x7F)
            {
                return AssetBoundedRefusal.InvalidOptions;
            }

            pathBytes[index] = (byte)value;
        }

        pathBytes[path.Length] = 0;
        fixed (byte* pathPointer = pathBytes)
        {
            var descriptor = LinuxNative.Open(
                pathPointer,
                LinuxOpenReadOnly | LinuxOpenCloseOnExec | LinuxOpenNonBlocking,
                0);

            if (descriptor < 0)
            {
                var error = Marshal.GetLastSystemError();
                primary = CreateFailure(
                    AssetBoundedFailureStage.Open,
                    AssetBoundedFailureKind.Native,
                    AssetBoundedNativeApi.LinuxOpen,
                    error,
                    AssetBoundedFileDisposition.NotAttempted);
                return AssetBoundedRefusal.None;
            }

            file.LinuxDescriptor = descriptor;
            file.Api = AssetBoundedNativeApi.LinuxOpen;
            file.CloseAttempted = false;
            file.CloseFailure = default;
            file.Disposition = AssetBoundedFileDisposition.NotFinal;
            file.Length = 0;
            file.PortUseCounted = true;
            _openFileCount++;
            file.WindowsHandle = -1;
        }

        while (LinuxNative.Flock(file.LinuxDescriptor, LinuxLockShared) != 0)
        {
            var error = Marshal.GetLastSystemError();

            if (error == LinuxEintr)
            {
                continue;
            }

            primary = CreateFailure(
                AssetBoundedFailureStage.NativeFileCheck,
                AssetBoundedFailureKind.Native,
                AssetBoundedNativeApi.LinuxFlock,
                error,
                file.Disposition);
            fileRelease = CloseFileCore(file, ref _openFileCount);
            return AssetBoundedRefusal.None;
        }

        if (LinuxNative.FileStatus(file.LinuxDescriptor, out var status) != 0)
        {
            var error = Marshal.GetLastSystemError();
            primary = CreateFailure(
                AssetBoundedFailureStage.NativeFileCheck,
                AssetBoundedFailureKind.Native,
                AssetBoundedNativeApi.LinuxFileStatus,
                error,
                file.Disposition);
            fileRelease = CloseFileCore(file, ref _openFileCount);
            return AssetBoundedRefusal.None;
        }

        if ((status.Mode & LinuxStatTypeMask) != LinuxRegularFile)
        {
            primary = CreateFailure(
                AssetBoundedFailureStage.NativeFileCheck,
                AssetBoundedFailureKind.UnsupportedContent,
                AssetBoundedNativeApi.LinuxFileStatus,
                0,
                file.Disposition);
            fileRelease = CloseFileCore(file, ref _openFileCount);
            return AssetBoundedRefusal.None;
        }

        if (status.SizeBytes is < MinimumPackageBytes or > MaximumPackageBytes)
        {
            primary = CreateFailure(
                AssetBoundedFailureStage.NativeFileCheck,
                AssetBoundedFailureKind.Layout,
                AssetBoundedNativeApi.LinuxFileStatus,
                0,
                file.Disposition);
            fileRelease = CloseFileCore(file, ref _openFileCount);
            return AssetBoundedRefusal.None;
        }

        file.Length = status.SizeBytes;
        return AssetBoundedRefusal.None;
    }

    private AssetBoundedRefusal OpenWindows(
        string path,
        AssetBoundedNativeFile file,
        out AssetBoundedFailureFact primary,
        out AssetBoundedFailureFact fileRelease)
    {
        primary = default;
        fileRelease = default;

        if (path.Length > 259)
        {
            return AssetBoundedRefusal.InvalidOptions;
        }

        nint handle = 0;
        fixed (char* pathPointer = path)
        {
            handle = WindowsNative.CreateFileW(
                pathPointer,
                WindowsGenericRead,
                WindowsShareRead,
                0,
                WindowsOpenExisting,
                WindowsFileFlagOverlapped | WindowsFileFlagRandomAccess,
                0);
        }

        if (handle == -1)
        {
            var error = Marshal.GetLastSystemError();
            primary = CreateFailure(
                AssetBoundedFailureStage.Open,
                AssetBoundedFailureKind.Native,
                AssetBoundedNativeApi.WindowsCreateFile,
                error,
                AssetBoundedFileDisposition.NotAttempted);
            return AssetBoundedRefusal.None;
        }

        file.WindowsHandle = handle;
        file.Api = AssetBoundedNativeApi.WindowsCreateFile;
        file.CloseAttempted = false;
        file.CloseFailure = default;
        file.Disposition = AssetBoundedFileDisposition.NotFinal;
        file.Length = 0;
        file.LinuxDescriptor = -1;
        file.PortUseCounted = true;
        _openFileCount++;
        var fileType = WindowsNative.GetFileType(handle);

        if (fileType != WindowsFileTypeDisk)
        {
            var error = (fileType == 0) ? Marshal.GetLastSystemError() : 0;
            primary = CreateFailure(
                AssetBoundedFailureStage.NativeFileCheck,
                AssetBoundedFailureKind.UnsupportedContent,
                AssetBoundedNativeApi.WindowsQueryFile,
                error,
                file.Disposition);
            fileRelease = CloseFileCore(file, ref _openFileCount);
            return AssetBoundedRefusal.None;
        }

        if (WindowsNative.GetStandardInfo(
            handle,
            fileInformationClass: 1,
            out var standardInfo,
            checked((uint)Unsafe.SizeOf<AssetBoundedWindowsStandardInfo>())) == 0)
        {
            var error = Marshal.GetLastSystemError();
            primary = CreateFailure(
                AssetBoundedFailureStage.NativeFileCheck,
                AssetBoundedFailureKind.Native,
                AssetBoundedNativeApi.WindowsQueryFile,
                error,
                file.Disposition);
            fileRelease = CloseFileCore(file, ref _openFileCount);
            return AssetBoundedRefusal.None;
        }

        if ((standardInfo.Directory != 0)
            || (standardInfo.EndOfFile < MinimumPackageBytes)
            || (standardInfo.EndOfFile > MaximumPackageBytes))
        {
            primary = CreateFailure(
                AssetBoundedFailureStage.NativeFileCheck,
                AssetBoundedFailureKind.UnsupportedContent,
                AssetBoundedNativeApi.WindowsQueryFile,
                0,
                file.Disposition);
            fileRelease = CloseFileCore(file, ref _openFileCount);
            return AssetBoundedRefusal.None;
        }

        file.Length = standardInfo.EndOfFile;
        return AssetBoundedRefusal.None;
    }

    private AssetBoundedFailureFact ReadExactlyCore(
        AssetBoundedNativeFile file,
        ulong offset,
        scoped Span<byte> destination,
        AssetBoundedFailureStage failureStage)
    {
        var read = 0;

        while (read < destination.Length)
        {
            var requested = Math.Min(NativeScratchBytes, destination.Length - read);
            var readFailure = ReadOneCore(
                file,
                checked(offset + checked((ulong)read)),
                requested,
                failureStage,
                out var bytesRead);

            if (readFailure.Kind is not AssetBoundedFailureKind.None)
            {
                return readFailure;
            }

            if (bytesRead == 0)
            {
                return CreateFailure(
                    failureStage,
                    AssetBoundedFailureKind.Layout,
                    file.Api,
                    0,
                    file.Disposition);
            }

            new ReadOnlySpan<byte>((void*)_scratch, bytesRead).CopyTo(destination[read..]);
            read = checked(read + bytesRead);
        }

        return default;
    }

    private AssetBoundedFailureFact ReadOneCore(
        AssetBoundedNativeFile file,
        ulong offset,
        int requested,
        AssetBoundedFailureStage failureStage,
        out int bytesRead)
    {
        bytesRead = 0;

        if (file.Api is AssetBoundedNativeApi.LinuxOpen)
        {
            while (true)
            {
                var nativeCount = LinuxNative.Pread(
                    file.LinuxDescriptor,
                    (void*)_scratch,
                    checked((nuint)requested),
                    checked((long)offset));

                if (nativeCount >= 0)
                {
                    bytesRead = checked((int)nativeCount);
                    return default;
                }

                var error = Marshal.GetLastSystemError();

                if (error == LinuxEintr)
                {
                    continue;
                }

                return CreateFailure(
                    failureStage,
                    AssetBoundedFailureKind.Native,
                    AssetBoundedNativeApi.LinuxPread,
                    error,
                    file.Disposition);
            }
        }

        _overlapped = default;
        _overlapped.Offset = unchecked((uint)offset);
        _overlapped.OffsetHigh = checked((uint)(offset >> 32));
        _overlapped.EventHandle = _ioEvent;

        if (WindowsNative.ResetEvent(_ioEvent) == 0)
        {
            var error = Marshal.GetLastSystemError();
            return CreateFailure(
                failureStage,
                AssetBoundedFailureKind.Native,
                AssetBoundedNativeApi.WindowsResetEvent,
                error,
                file.Disposition);
        }

        var accepted = WindowsNative.ReadFile(
            file.WindowsHandle,
            (void*)_scratch,
            checked((uint)requested),
            out var immediateBytes,
            ref _overlapped);

        if (accepted != 0)
        {
            bytesRead = checked((int)immediateBytes);
            return default;
        }

        var readError = Marshal.GetLastSystemError();

        if (readError != WindowsIoPending)
        {
            return CreateFailure(
                failureStage,
                AssetBoundedFailureKind.Native,
                AssetBoundedNativeApi.WindowsReadFile,
                readError,
                file.Disposition);
        }

        while (true)
        {
            if (WindowsNative.GetOverlappedResult(
                file.WindowsHandle,
                ref _overlapped,
                out var transferred,
                wait: 1) != 0)
            {
                bytesRead = checked((int)transferred);
                return default;
            }

            var completionError = Marshal.GetLastSystemError();

            if (completionError == WindowsIoIncomplete)
            {
                continue;
            }

            return CreateFailure(
                failureStage,
                AssetBoundedFailureKind.Native,
                AssetBoundedNativeApi.WindowsGetOverlappedResult,
                completionError,
                file.Disposition);
        }
    }

    private AssetBoundedRefusal TryEnter()
    {
        if (!IsSupportedTarget())
        {
            return AssetBoundedRefusal.ProfileUnavailable;
        }

        if (!IsOwnerThread)
        {
            return AssetBoundedRefusal.UnsupportedContext;
        }

        return (Interlocked.CompareExchange(ref _activeOperation, 1, 0) == 0)
            ? AssetBoundedRefusal.None
            : AssetBoundedRefusal.Busy;
    }

    [SuppressMessage(
        "Security",
        "CA5392:Use DefaultDllImportSearchPaths attribute",
        Justification = "The libc binding is restricted to the selected Linux native package port.")]
    private static partial class LinuxNative
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        internal static partial int Close(int descriptor);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
        internal static partial int FileStatus(int descriptor, out AssetBoundedLinuxStatX64 status);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
        internal static partial int Flock(int descriptor, int operation);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "open", SetLastError = true)]
        internal static unsafe partial int Open(byte* path, int flags, uint mode);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [LibraryImport("libc", EntryPoint = "pread", SetLastError = true)]
        internal static unsafe partial nint Pread(int descriptor, void* buffer, nuint count, long offset);
    }

    private static partial class WindowsNative
    {
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
        internal static partial int CloseHandle(nint handle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", SetLastError = true)]
        internal static partial nint CreateEventW(nint securityAttributes, int manualReset, int initialState, nint name);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true)]
        internal static unsafe partial nint CreateFileW(
            char* path,
            uint desiredAccess,
            uint shareMode,
            nint securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            nint templateFile);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "GetFileType", SetLastError = true)]
        internal static partial uint GetFileType(nint handle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "GetOverlappedResult", SetLastError = true)]
        internal static partial int GetOverlappedResult(
            nint file,
            ref AssetBoundedWindowsOverlapped overlapped,
            out uint numberOfBytesTransferred,
            int wait);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
        internal static partial int GetStandardInfo(
            nint handle,
            int fileInformationClass,
            out AssetBoundedWindowsStandardInfo fileInformation,
            uint bufferSize);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "ReadFile", SetLastError = true)]
        internal static unsafe partial int ReadFile(
            nint file,
            void* buffer,
            uint numberOfBytesToRead,
            out uint numberOfBytesRead,
            ref AssetBoundedWindowsOverlapped overlapped);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [LibraryImport("kernel32.dll", EntryPoint = "ResetEvent", SetLastError = true)]
        internal static partial int ResetEvent(nint eventHandle);
    }
}
