// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Assets;

internal sealed class AssetStoreBoundedProvision
{
    private const int PackageDirectoryBufferBytes = 128;
    private const int PackageProfileBufferBytes = 65_536;
    private const int StateBound = 1;
    private const int StateEnded = 4;
    private const int StateFailed = 3;
    private const int StatePreclaimed = 0;

    private string? _absolutePackagePath;
    private readonly long _cpuAssetBudgetBytes;
    private byte[]? _expectedPackageSha256;
    private AssetBoundedFailureFact _failure;
    private readonly int _maximumConcurrentDecodeBytes;
    private readonly int _maximumDecodedAssetBytes;
    private readonly int _maximumLiveAssetLeases;
    private readonly AssetBoundedNativePort _nativePort;
    private readonly bool _ownsSharedProvision;
    private readonly byte[] _packageDirectoryBuffer;
    private readonly byte[] _packageProfileBuffer;
    private AssetStoreRegistration? _registration;
    private readonly long _reservedBytes;
    private readonly long _sharedProvisionBytes;
    private int _state;
    private AssetBoundedPreclaimRelease _terminalPreclaimRelease;

    internal AssetStoreBoundedProvision(
        string absolutePackagePath,
        byte[] expectedPackageSha256,
        long cpuAssetBudgetBytes,
        int maximumDecodedAssetBytes,
        int maximumConcurrentDecodeBytes,
        int maximumLiveAssetLeases,
        long reservedBytes,
        long sharedProvisionBytes,
        bool ownsSharedProvision)
    {
        _absolutePackagePath = absolutePackagePath;
        _expectedPackageSha256 = expectedPackageSha256;
        _cpuAssetBudgetBytes = cpuAssetBudgetBytes;
        _maximumDecodedAssetBytes = maximumDecodedAssetBytes;
        _maximumConcurrentDecodeBytes = maximumConcurrentDecodeBytes;
        _maximumLiveAssetLeases = maximumLiveAssetLeases;
        _nativePort = new AssetBoundedNativePort(Environment.CurrentManagedThreadId);
        _packageDirectoryBuffer = new byte[PackageDirectoryBufferBytes];
        _packageProfileBuffer = new byte[PackageProfileBufferBytes];
        _reservedBytes = reservedBytes;
        _sharedProvisionBytes = sharedProvisionBytes;
        _ownsSharedProvision = ownsSharedProvision;
        _state = StatePreclaimed;
    }

    internal long CpuAssetBudgetBytes => _cpuAssetBudgetBytes;

    internal ReadOnlyMemory<byte> ExpectedPackageSha256
        => _expectedPackageSha256
            ?? throw new ObjectDisposedException(nameof(AssetStoreBoundedProvision));

    internal int MaximumConcurrentDecodeBytes => _maximumConcurrentDecodeBytes;

    internal int MaximumDecodedAssetBytes => _maximumDecodedAssetBytes;

    internal int MaximumLiveAssetLeases => _maximumLiveAssetLeases;

    internal AssetBoundedNativePort NativePort => _nativePort;

    internal byte[] PackageDirectoryBuffer => _packageDirectoryBuffer;

    internal string PackagePath
        => _absolutePackagePath
            ?? throw new ObjectDisposedException(nameof(AssetStoreBoundedProvision));

    internal Span<byte> PackageProfileBuffer => _packageProfileBuffer;

    internal AssetStoreRegistration Registration
        => _registration
            ?? throw new InvalidOperationException("The bounded Assets registration has not been installed.");

    internal long ReservedBytes => _reservedBytes;

    internal long SharedProvisionBytes => _sharedProvisionBytes;

    internal void FailInitialization(AssetBoundedFailureFact primary)
    {
        if (_state is not StatePreclaimed)
        {
            throw new InvalidOperationException("The bounded Assets initialization is no longer active.");
        }

        _failure = primary;
        _state = StateFailed;
        AssetStoreBoundedBootstrap.Fail(_ownsSharedProvision);
    }

    internal AssetBoundedPreclaimRelease ReleaseUnstartedPreclaim()
    {
        if (_state is StateEnded)
        {
            return _terminalPreclaimRelease;
        }

        var state = _state;

        if (state is not (StatePreclaimed or StateFailed or StateBound))
        {
            throw new InvalidOperationException("The bounded Assets preclaim cannot release from its current state.");
        }

        if (_nativePort.InitializationAttempted)
        {
            throw new InvalidOperationException("The bounded Assets preclaim cannot release after native port initialization began.");
        }

        var hasFailure = state is StateFailed;
        var failure = _failure;
        var retainedSharedProvisionBytes = 0L;

        if (state is StatePreclaimed)
        {
            AssetStoreBoundedBootstrap.Cancel(_ownsSharedProvision);
        }
        else if (state is StateFailed or StateBound)
        {
            retainedSharedProvisionBytes = _ownsSharedProvision ? _sharedProvisionBytes : 0;
        }

        _absolutePackagePath = null;
        _expectedPackageSha256 = null;
        _registration = null;
        _state = StateEnded;
        _terminalPreclaimRelease = new AssetBoundedPreclaimRelease(
            ownerReleased: true,
            AssetBoundedRefusal.None,
            hasFailure,
            failure,
            retainedSharedProvisionBytes);
        return _terminalPreclaimRelease;
    }

    internal bool TryBindRegistration(AssetStoreRegistration registration)
    {
        if ((_state != StatePreclaimed)
            || (registration.Family != AssetStoreRegistrationFamily.StaticMeshValidationV1)
            || !AssetStoreBoundedBootstrap.CompleteInitialization(_ownsSharedProvision))
        {
            return false;
        }

        _registration = registration;
        _state = StateBound;
        return true;
    }
}
