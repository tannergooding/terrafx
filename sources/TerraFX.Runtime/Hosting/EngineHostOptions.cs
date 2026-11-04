// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using TerraFX.Assets;
using TerraFX.Graphics;
using TerraFX.Platform;
using ClipboardServicePolicyKind = TerraFX.Platform.ClipboardServicePolicy;
using GraphicsBackendKind = TerraFX.Graphics.GraphicsBackend;
using GraphicsFailurePolicyKind = TerraFX.Hosting.GraphicsFailurePolicy;
using SimulationOverrunPolicyKind = TerraFX.Hosting.SimulationOverrunPolicy;

namespace TerraFX.Hosting;

/// <summary>Contains immutable service selection and bounded host scheduling options.</summary>
public sealed class EngineHostOptions
{
    private const int MaximumCpuWorkerCount = 64;
    private ClipboardServicePolicy _clipboardServicePolicy = ClipboardServicePolicyKind.NotRequested;
    private int _cpuWorkerCount = Math.Min(MaximumCpuWorkerCount, Math.Max(1, Environment.ProcessorCount - 2));
    private GraphicsFailurePolicy _graphicsFailurePolicy = GraphicsFailurePolicyKind.StopHost;
    private SimulationOverrunPolicy _overrunPolicy = SimulationOverrunPolicyKind.Pause;
    private TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Initializes an explicit host profile, platform, graphics selection, and package selection.</summary>
    /// <param name="profile">The requested host profile.</param>
    /// <param name="platform">The explicitly selected platform backend.</param>
    /// <param name="graphicsBackend">The selected graphics backend, or null for Headless.</param>
    /// <param name="graphicsBackendRegistration">The matching registration, or null for Headless.</param>
    /// <param name="assetStoreOptions">The trusted package selection and codec registration.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assetStoreOptions"/> is null.</exception>
    /// <exception cref="ArgumentException">The profile, platform, backend, and registration combination is inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A profile or backend selector is invalid.</exception>
    public EngineHostOptions(
        HostProfile profile,
        PlatformBackend platform,
        GraphicsBackend? graphicsBackend,
        GraphicsBackendRegistration? graphicsBackendRegistration,
        AssetStoreOptions assetStoreOptions)
    {
        ArgumentNullException.ThrowIfNull(assetStoreOptions);
        ValidateServiceSelection(profile, platform, graphicsBackend, graphicsBackendRegistration);

        Profile = profile;
        Platform = platform;
        GraphicsBackend = graphicsBackend;
        GraphicsBackendRegistration = graphicsBackendRegistration;
        AssetStoreOptions = assetStoreOptions;
    }

    /// <summary>Gets the trusted package selection and codec registration.</summary>
    public AssetStoreOptions AssetStoreOptions { get; }

    /// <summary>Gets or initializes native clipboard admission policy.</summary>
    public ClipboardServicePolicy ClipboardServicePolicy
    {
        get
        {
            return _clipboardServicePolicy;
        }
        init
        {
            if (value is not (ClipboardServicePolicyKind.NotRequested
                or ClipboardServicePolicyKind.Optional
                or ClipboardServicePolicyKind.Required))
            {
                throw new ArgumentOutOfRangeException(nameof(ClipboardServicePolicy));
            }

            _clipboardServicePolicy = value;
        }
    }

    /// <summary>Gets or initializes the bounded CPU worker count in [0, 64]; zero runs partitions inline.</summary>
    public int CpuWorkerCount
    {
        get
        {
            return _cpuWorkerCount;
        }
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaximumCpuWorkerCount);

            _cpuWorkerCount = value;
        }
    }

    /// <summary>Gets the selected graphics backend, or null for Headless.</summary>
    public GraphicsBackend? GraphicsBackend { get; }

    /// <summary>Gets the selected backend's immutable registration, or null for Headless.</summary>
    public GraphicsBackendRegistration? GraphicsBackendRegistration { get; }

    /// <summary>Gets or initializes graphics-device failure policy.</summary>
    public GraphicsFailurePolicy GraphicsFailurePolicy
    {
        get
        {
            return _graphicsFailurePolicy;
        }
        init
        {
            if (value != GraphicsFailurePolicyKind.StopHost)
            {
                throw new ArgumentOutOfRangeException(nameof(GraphicsFailurePolicy));
            }

            _graphicsFailurePolicy = value;
        }
    }

    /// <summary>Gets or initializes simulation overrun policy.</summary>
    public SimulationOverrunPolicy OverrunPolicy
    {
        get
        {
            return _overrunPolicy;
        }
        init
        {
            if (value is not (SimulationOverrunPolicyKind.Pause or SimulationOverrunPolicyKind.DropDebt))
            {
                throw new ArgumentOutOfRangeException(nameof(OverrunPolicy));
            }

            _overrunPolicy = value;
        }
    }

    /// <summary>Gets the application and runtime identity used by host-owned persistence.</summary>
    public required WorldPersistenceMetadata PersistenceMetadata { get; init; }

    /// <summary>Gets the explicitly selected platform backend.</summary>
    public PlatformBackend Platform { get; }

    /// <summary>Gets the requested host execution profile.</summary>
    public HostProfile Profile { get; }

    /// <summary>Gets or initializes the positive finite shutdown wait bound.</summary>
    public TimeSpan ShutdownTimeout
    {
        get
        {
            return _shutdownTimeout;
        }
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);

            _shutdownTimeout = value;
        }
    }

    private static void ValidateServiceSelection(
        HostProfile profile,
        PlatformBackend platform,
        GraphicsBackend? graphicsBackend,
        GraphicsBackendRegistration? graphicsBackendRegistration)
    {
        if ((profile == HostProfile.None) || !Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        if ((platform == PlatformBackend.None) || !Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform));
        }

        if ((graphicsBackend is GraphicsBackend selectedBackend)
            && ((selectedBackend == GraphicsBackendKind.None) || !Enum.IsDefined(selectedBackend)))
        {
            throw new ArgumentOutOfRangeException(nameof(graphicsBackend));
        }

        if (profile == HostProfile.Headless)
        {
            if (platform != PlatformBackend.Headless)
            {
                throw new ArgumentException("Headless requires the Headless platform backend.", nameof(platform));
            }

            if ((graphicsBackend is not null) || (graphicsBackendRegistration is not null))
            {
                throw new ArgumentException("Headless does not select a graphics backend or registration.", nameof(graphicsBackend));
            }

            return;
        }

        if (profile == HostProfile.OffscreenNative)
        {
            if (platform != PlatformBackend.Headless)
            {
                throw new ArgumentException("OffscreenNative requires the Headless platform backend.", nameof(platform));
            }
        }
        else if (platform == PlatformBackend.Headless)
        {
            throw new ArgumentException("Interactive requires a native display platform backend.", nameof(platform));
        }

        if ((graphicsBackend is null) || (graphicsBackendRegistration is null))
        {
            throw new ArgumentException("Native profiles require both a graphics backend and its registration.", nameof(graphicsBackend));
        }

        if (graphicsBackendRegistration.Backend != graphicsBackend.Value)
        {
            throw new ArgumentException("The graphics registration must match the selected backend.", nameof(graphicsBackendRegistration));
        }

        if ((profile == HostProfile.Interactive)
            && (graphicsBackend == GraphicsBackendKind.D3D12)
            && (platform != PlatformBackend.WindowsNative))
        {
            throw new ArgumentException("Interactive D3D12 requires the Windows native platform backend.", nameof(platform));
        }
    }
}
