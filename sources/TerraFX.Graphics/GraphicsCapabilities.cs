// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;

namespace TerraFX.Graphics;

/// <summary>Contains copied capabilities for one admitted native graphics device.</summary>
public sealed class GraphicsCapabilities
{
    private readonly ImmutableArray<GraphicsFormatCapabilities> _formats;
    private readonly ImmutableArray<GraphicsFeature> _hardwareFeatures;
    private readonly ImmutableArray<GraphicsFeature> _implementedFeatures;
    private readonly ImmutableArray<GraphicsMemoryHeapCapabilities> _memoryHeaps;

    internal GraphicsCapabilities(
        GraphicsBackend backend,
        GraphicsDeviceProfile profile,
        Guid adapterId,
        uint vendorId,
        uint deviceId,
        string adapterName,
        string driverName,
        string driverVersion,
        GraphicsApiVersion apiVersion,
        ImmutableArray<GraphicsFeature> hardwareFeatures,
        ImmutableArray<GraphicsFeature> implementedFeatures,
        GraphicsLimits limits,
        ImmutableArray<GraphicsFormatCapabilities> formats,
        ImmutableArray<GraphicsMemoryHeapCapabilities> memoryHeaps)
    {
        if ((backend == GraphicsBackend.None) || !Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }

        if ((profile == GraphicsDeviceProfile.None) || !Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        ArgumentOutOfRangeException.ThrowIfEqual(adapterId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverVersion);
        ArgumentNullException.ThrowIfNull(limits);

        if ((apiVersion.Major == 0)
            || hardwareFeatures.IsDefaultOrEmpty
            || implementedFeatures.IsDefaultOrEmpty
            || formats.IsDefaultOrEmpty
            || memoryHeaps.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Graphics capability values and arrays must be initialized.");
        }

        var sortedHardwareFeatures = hardwareFeatures.ToArray();
        Array.Sort(sortedHardwareFeatures);
        ValidateUniqueFeatures(sortedHardwareFeatures, nameof(hardwareFeatures));
        var sortedImplementedFeatures = implementedFeatures.ToArray();
        Array.Sort(sortedImplementedFeatures);
        ValidateUniqueFeatures(sortedImplementedFeatures, nameof(implementedFeatures));
        var sortedFormats = formats.ToArray();
        ValidateNotNull(sortedFormats, nameof(formats));
        Array.Sort(sortedFormats, static (left, right) => left.Format.CompareTo(right.Format));
        ValidateUniqueFormats(sortedFormats);
        var sortedMemoryHeaps = memoryHeaps.ToArray();
        ValidateNotNull(sortedMemoryHeaps, nameof(memoryHeaps));
        Array.Sort(sortedMemoryHeaps, static (left, right) => left.Index.CompareTo(right.Index));
        ValidateUniqueMemoryHeaps(sortedMemoryHeaps);
        ValidateImplementedFeatures(sortedHardwareFeatures, sortedImplementedFeatures);

        Backend = backend;
        Profile = profile;
        AdapterId = adapterId;
        VendorId = vendorId;
        DeviceId = deviceId;
        AdapterName = adapterName;
        DriverName = driverName;
        DriverVersion = driverVersion;
        ApiVersion = apiVersion;
        _hardwareFeatures = [.. sortedHardwareFeatures];
        _implementedFeatures = [.. sortedImplementedFeatures];
        Limits = limits;
        _formats = [.. sortedFormats];
        _memoryHeaps = [.. sortedMemoryHeaps];
    }

    /// <summary>Gets the native identity of the selected adapter.</summary>
    public Guid AdapterId { get; }

    /// <summary>Gets the adapter name reported by the selected backend.</summary>
    public string AdapterName { get; }

    /// <summary>Gets the backend API version.</summary>
    public GraphicsApiVersion ApiVersion { get; }

    /// <summary>Gets the selected backend.</summary>
    public GraphicsBackend Backend { get; }

    /// <summary>Gets the native device identifier reported by the backend.</summary>
    public uint DeviceId { get; }

    /// <summary>Gets the driver name reported by the backend.</summary>
    public string DriverName { get; }

    /// <summary>Gets the driver version reported by the backend.</summary>
    public string DriverVersion { get; }

    /// <summary>Gets the supported texture format/usages in numeric format order.</summary>
    public ImmutableArray<GraphicsFormatCapabilities> Formats => _formats;

    /// <summary>Gets the supported hardware feature declarations in numeric order.</summary>
    public ImmutableArray<GraphicsFeature> HardwareFeatures => _hardwareFeatures;

    /// <summary>Gets the features implemented by the selected TerraFX backend in numeric order.</summary>
    public ImmutableArray<GraphicsFeature> ImplementedFeatures => _implementedFeatures;

    /// <summary>Gets the actual device limits and alignments.</summary>
    public GraphicsLimits Limits { get; }

    /// <summary>Gets the supported memory heaps in native heap-index order.</summary>
    public ImmutableArray<GraphicsMemoryHeapCapabilities> MemoryHeaps => _memoryHeaps;

    /// <summary>Gets the profile admitted for this device.</summary>
    public GraphicsDeviceProfile Profile { get; }

    /// <summary>Gets the native vendor identifier.</summary>
    public uint VendorId { get; }

    private static void ValidateImplementedFeatures(
        ReadOnlySpan<GraphicsFeature> hardwareFeatures,
        ReadOnlySpan<GraphicsFeature> implementedFeatures)
    {
        for (var index = 0; index < implementedFeatures.Length; index++)
        {
            if (!hardwareFeatures.Contains(implementedFeatures[index]))
            {
                throw new ArgumentException("Implemented graphics features must be supported by the hardware.", nameof(implementedFeatures));
            }
        }
    }

    private static void ValidateNotNull<T>(ReadOnlySpan<T?> values, string parameterName)
        where T : class
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] is null)
            {
                throw new ArgumentException("Graphics capability arrays cannot contain null values.", parameterName);
            }
        }
    }

    private static void ValidateUniqueFeatures(
        ReadOnlySpan<GraphicsFeature> features,
        string parameterName)
    {
        for (var index = 0; index < features.Length; index++)
        {
            if ((features[index] == GraphicsFeature.None) || !Enum.IsDefined(features[index]))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            if ((index > 0) && (features[index] == features[index - 1]))
            {
                throw new ArgumentException("Graphics feature arrays must contain unique values.", parameterName);
            }
        }
    }

    private static void ValidateUniqueFormats(ReadOnlySpan<GraphicsFormatCapabilities> formats)
    {
        for (var index = 1; index < formats.Length; index++)
        {
            if (formats[index].Format == formats[index - 1].Format)
            {
                throw new ArgumentException("Graphics format capabilities must contain unique formats.", nameof(formats));
            }
        }
    }

    private static void ValidateUniqueMemoryHeaps(ReadOnlySpan<GraphicsMemoryHeapCapabilities> memoryHeaps)
    {
        for (var index = 1; index < memoryHeaps.Length; index++)
        {
            if (memoryHeaps[index].Index == memoryHeaps[index - 1].Index)
            {
                throw new ArgumentException("Graphics memory heaps must have unique native indices.", nameof(memoryHeaps));
            }
        }
    }
}
