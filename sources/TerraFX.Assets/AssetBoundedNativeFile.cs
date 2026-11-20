// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Assets;

internal sealed class AssetBoundedNativeFile
{
    internal AssetBoundedNativeApi Api;
    internal bool CloseAttempted;
    internal AssetBoundedFailureFact CloseFailure;
    internal AssetBoundedFileDisposition Disposition;
    internal long Length;
    internal int LinuxDescriptor;
    internal AssetBoundedNativePort? Port;
    internal bool PortUseCounted;
    internal nint WindowsHandle;

    internal bool IsOpen
        => !CloseAttempted
            && (Disposition is AssetBoundedFileDisposition.NotFinal)
            && PortUseCounted
            && (Api is AssetBoundedNativeApi.WindowsCreateFile or AssetBoundedNativeApi.LinuxOpen);
}
