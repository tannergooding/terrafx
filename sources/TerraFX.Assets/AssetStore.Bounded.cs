// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Assets;

public sealed partial class AssetStore
{
    private const long BoundedControlByteReservation = 528;
    private const long BoundedNativeFileStateByteReservation = 80;
    private const long BoundedNativePortByteReservation = 144;
    private const long BoundedOpeningBufferByteReservation = 131_343;

    internal long BoundedReservedBytes
    {
        get
        {
            lock (_gate)
            {
                return _currentBacking?.BoundedReservedBytes ?? 0;
            }
        }
    }

    internal static AssetBoundedRefusal TryPreclaimBoundedStaticMeshValidationV1(
        string absolutePackagePath,
        ReadOnlySpan<byte> expectedPackageSha256,
        long cpuAssetBudgetBytes,
        int maximumDecodedAssetBytes,
        int maximumConcurrentDecodeBytes,
        int maximumLiveAssetLeases,
        long runtimePerStoreProvisionBytes,
        long runtimeSharedProvisionBytes,
        out AssetStore? issuer,
        CancellationToken cancellationToken)
    {
        issuer = null;

        if (cancellationToken.CanBeCanceled)
        {
            return AssetBoundedRefusal.UnsupportedToken;
        }

        if ((SynchronizationContext.Current is not null)
            || (OperatingSystem.IsWindows()
                && (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)))
        {
            return AssetBoundedRefusal.UnsupportedContext;
        }

        if (!Environment.Is64BitProcess)
        {
            return AssetBoundedRefusal.ProfileUnavailable;
        }

        if (!IsSupportedPackagePath(absolutePackagePath))
        {
            return OperatingSystem.IsWindows() || OperatingSystem.IsLinux()
                ? AssetBoundedRefusal.InvalidOptions
                : AssetBoundedRefusal.ProfileUnavailable;
        }

        if ((expectedPackageSha256.Length != 32)
            || (cpuAssetBudgetBytes < 0)
            || (cpuAssetBudgetBytes > 8_589_934_592L)
            || (maximumDecodedAssetBytes < 0)
            || (maximumDecodedAssetBytes > 268_435_456)
            || (maximumConcurrentDecodeBytes < 0)
            || (maximumConcurrentDecodeBytes > 268_435_456)
            || (maximumLiveAssetLeases < 0)
            || (maximumLiveAssetLeases > 65_536)
            || (runtimePerStoreProvisionBytes <= 0)
            || (runtimeSharedProvisionBytes <= 0))
        {
            return AssetBoundedRefusal.InvalidOptions;
        }

        var pathBytes = GetStringStorageByteCharge(absolutePackagePath.Length);
        var digestBytes = GetArrayStorageByteCharge(expectedPackageSha256.Length);
        var localProvisionBytes = checked(
            BoundedControlByteReservation
            + BoundedOpeningBufferByteReservation
            + BoundedNativeFileStateByteReservation
            + BoundedNativePortByteReservation
            + pathBytes
            + digestBytes
            + runtimePerStoreProvisionBytes);

        var preclaimRefusal = AssetStoreBoundedBootstrap.TryBegin(
            cpuAssetBudgetBytes,
            localProvisionBytes,
            runtimeSharedProvisionBytes,
            out var ownsSharedProvision,
            out var reservedBytes);

        if (preclaimRefusal is not AssetBoundedRefusal.None)
        {
            return preclaimRefusal;
        }

        try
        {
            var pathCopy = new string(absolutePackagePath.AsSpan());
            var digestCopy = expectedPackageSha256.ToArray();
            var boundedProvision = new AssetStoreBoundedProvision(
                pathCopy,
                digestCopy,
                cpuAssetBudgetBytes,
                maximumDecodedAssetBytes,
                maximumConcurrentDecodeBytes,
                maximumLiveAssetLeases,
                reservedBytes,
                runtimeSharedProvisionBytes,
                ownsSharedProvision);
            var openingBacking = new AssetPackageBacking(boundedProvision);
            issuer = new AssetStore(openingBacking, isBoundedOpening: true);
            return AssetBoundedRefusal.None;
        }
        catch (OutOfMemoryException)
        {
            AssetStoreBoundedBootstrap.Cancel(ownsSharedProvision);
            return AssetBoundedRefusal.Capacity;
        }
    }

    private static long GetArrayStorageByteCharge(int length)
    {
        var allocationBytes = checked(24L + length);
        return checked((allocationBytes + 7L) & ~7L);
    }

    private static long GetStringStorageByteCharge(int characterCount)
    {
        var allocationBytes = checked(24L + ((long)characterCount * sizeof(char)));
        return checked((allocationBytes + 7L) & ~7L);
    }

    private static bool IsAsciiLetter(char value)
        => value is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    private static bool IsNumberedDeviceComponent(ReadOnlySpan<char> stem, ReadOnlySpan<char> prefix)
        => (stem.Length == prefix.Length + 1)
            && stem[..prefix.Length].Equals(prefix, StringComparison.OrdinalIgnoreCase)
            && (stem[prefix.Length] is >= '1' and <= '9');

    private static bool IsReservedDeviceComponent(ReadOnlySpan<char> stem)
        => stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || IsNumberedDeviceComponent(stem, "COM")
            || IsNumberedDeviceComponent(stem, "LPT");

    private static bool IsSupportedPackagePath(string? path)
    {
        if (path is null)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return (path.Length is >= 4 and <= 259)
                && IsAsciiLetter(path[0])
                && (path[1] == ':')
                && (path[2] == '\\')
                && IsSupportedPathComponents(path.AsSpan(), 3, '\\');
        }

        if (OperatingSystem.IsLinux())
        {
            return (path.Length is >= 2 and <= 4095)
                && (path[0] == '/')
                && IsSupportedPathComponents(path.AsSpan(), 1, '/');
        }

        return false;
    }

    private static bool IsSupportedPathComponent(ReadOnlySpan<char> component)
    {
        var segmentLength = 0;
        var firstDot = -1;

        for (var index = 0; index < component.Length; index++)
        {
            var value = component[index];

            if (value == '.')
            {
                if (segmentLength == 0)
                {
                    return false;
                }

                firstDot = (firstDot < 0) ? index : firstDot;
                segmentLength = 0;
                continue;
            }

            if (!IsAsciiLetter(value)
                && (value is not (>= '0' and <= '9'))
                && (value != '_')
                && (value != '-'))
            {
                return false;
            }

            segmentLength++;
        }

        if (segmentLength == 0)
        {
            return false;
        }

        var stem = (firstDot < 0) ? component : component[..firstDot];
        return !IsReservedDeviceComponent(stem);
    }

    private static bool IsSupportedPathComponents(ReadOnlySpan<char> path, int start, char separator)
    {
        while (start < path.Length)
        {
            var remaining = path[start..];
            var separatorIndex = remaining.IndexOf(separator);
            var component = (separatorIndex < 0) ? remaining : remaining[..separatorIndex];

            if (!IsSupportedPathComponent(component))
            {
                return false;
            }

            if (separatorIndex < 0)
            {
                return true;
            }

            start = checked(start + separatorIndex + 1);
        }

        return false;
    }

    internal void FailBoundedInitialization(AssetBoundedFailureFact primary)
    {
        lock (_gate)
        {
            _currentBacking?.FailBoundedInitialization(primary);
        }
    }

    internal AssetBoundedPreclaimRelease ReleaseUnstartedBoundedPreclaim()
    {
        lock (_gate)
        {
            var backing = _currentBacking
                ?? throw new InvalidOperationException("The bounded Assets preclaim has already ended.");
            var release = backing.ReleaseUnstartedBoundedPreclaim();
            _currentBacking = null;
            return release;
        }
    }

    internal bool TryBindBoundedStaticMeshRegistration(AssetStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        lock (_gate)
        {
            return (_currentBacking is { } backing)
                && backing.TryBindBoundedStaticMeshRegistration(registration);
        }
    }
}
