// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using TerraFX;
using TerraFX.Graphics;
using TerraFX.Numerics;

namespace TerraFX.Rendering;

/// <summary>Declares range-scoped GPU work for one immutable render snapshot.</summary>
public sealed class RenderGraph
{
    private const string AccessInvalidCode = "render-graph.access-invalid";
    private const string DependencyCycleCode = "render-graph.dependency-cycle";
    private const string ImportInvalidCode = "render-graph.import-invalid";
    private const string PresentationSourceInvalidCode = "render-graph.presentation-source-invalid";
    private const string UninitializedReadCode = "render-graph.uninitialized-read";
    private static long s_nextGraphOwnerId;
    private readonly List<RenderGraphAttachmentWrite> _attachmentWrites = [];
    private readonly List<Diagnostic> _buildWarnings = [];
    private readonly List<RenderMeshGraphDeferredTexture> _builtInMeshDeferredTextures = [];
    private readonly Dictionary<RenderPassId, RenderMeshC0GraphPass> _builtInMeshPasses = [];
    private readonly Dictionary<RenderPassId, RenderMeshC0ShadowGraphPass> _builtInMeshShadowPasses = [];
    private RenderMeshC0NormalDiagnosticGraphPass? _builtInMeshNormalDiagnosticPass;
    private RenderPassId? _builtInMeshNormalDiagnosticPassId;
    private readonly List<RenderSpriteGraphBufferUpload> _builtInSpriteBufferUploads = [];
    private readonly List<RenderSpriteGraphDeferredTexture> _builtInSpriteDeferredTextures = [];
    private readonly Dictionary<RenderPassId, RenderSpriteC0GraphPass> _builtInSpritePasses = [];
    private readonly Dictionary<RenderPassId, RenderSpriteToneMapGraphPass> _builtInSpriteToneMapPasses = [];
    private RenderGraphCompileResponse? _compiledResponse;
    private readonly uint _graphOwnerId;
    private bool _isSealed;
    private readonly GraphTextureVersion? _offscreenColorTargetVersion;
    private readonly RenderGraphOptions _options;
    private readonly Dictionary<RenderPassId, RenderPassCallback> _passCallbacks = [];
    private readonly Dictionary<RenderPassId, RenderGraphPassKind> _passKinds = [];
    private readonly Dictionary<RenderPassId, RenderArea?> _passRenderAreas = [];
    private readonly HashSet<ulong> _nonAliasableTransientBufferIds = [];
    private readonly HashSet<ulong> _nonAliasableTransientTextureIds = [];
    private GraphBufferReadbackExport? _bufferReadbackExport;
    private GraphTextureVersion? _builtInSurfaceColorTargetVersion;
    private GraphTextureVersion? _defaultPresentationSource;
    private RenderPassId? _presentationPassId;
    private GraphTextureVersion? _presentationSource;
    private readonly GraphResourceRegistry _resources;

    /// <summary>Initializes a graph from immutable snapshot metadata and one typed output target.</summary>
    /// <param name="options">The snapshot identity and output-target contract.</param>
    /// <exception cref="ArgumentException">The options do not select an output target.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The selected GraphicsDevice is not ready.</exception>
    public RenderGraph(RenderGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _graphOwnerId = AllocateGraphOwnerId();
        var device = options.Surface?.Device
            ?? options.OffscreenColorTarget?.ResourceOwner.Device
            ?? throw new ArgumentException("The graph options do not select an output target.", nameof(options));
        _resources = new GraphResourceRegistry(device, _graphOwnerId);

        if (options.OffscreenColorTarget is GpuTexture target)
        {
            var outputRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);
            (var initialState, var contentsValid, _, _) = target.ResourceOwner.CaptureGraphImportState(outputRange);
            _offscreenColorTargetVersion = _resources.ImportTexture(
                target,
                outputRange,
                new GraphImportOptions(initialState, contentsValid, GraphImportCompletionPolicy.WaitForLastUse));
        }
    }

    /// <summary>Gets the automatically imported mip-0/layer-0 color version of an offscreen target.</summary>
    /// <exception cref="InvalidOperationException">This graph targets a Surface.</exception>
    public GraphTextureVersion OffscreenColorTargetVersion
        => _offscreenColorTargetVersion
            ?? throw new InvalidOperationException("A Surface-targeted graph has no offscreen color target version.");

    /// <summary>Gets the selected presentation source version.</summary>
    public GraphTextureVersion? PresentationSource => _presentationSource;

    internal GraphBufferReadbackExport? BufferReadbackExport => _bufferReadbackExport;

    internal ImmutableArray<ulong> NonAliasableTransientBufferIds
        => [.. _nonAliasableTransientBufferIds];

    internal ImmutableArray<ulong> NonAliasableTransientTextureIds
        => [.. _nonAliasableTransientTextureIds];

    private static uint AllocateGraphOwnerId()
    {
        var graphOwnerId = Interlocked.Increment(ref s_nextGraphOwnerId);

        if (graphOwnerId is <= 0 or > uint.MaxValue)
        {
            throw new InvalidOperationException("The process exhausted its render-graph identity range.");
        }

        return (uint)graphOwnerId;
    }

    private static bool IsFullRenderArea(
        RenderArea area,
        TextureDescription description,
        GraphTextureRange range)
    {
        var mipWidth = description.Width;
        var mipHeight = description.Height;
        var coversFullRange = true;
        var mipEnd = range.FirstMip + range.MipCount;

        for (var mip = 0; mip < mipEnd; mip++)
        {
            if (mip >= range.FirstMip
                && ((area.X != 0)
                    || (area.Y != 0)
                    || (area.Width != mipWidth)
                    || (area.Height != mipHeight)))
            {
                coversFullRange = false;
            }

            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }

        return coversFullRange;
    }

    private static void ValidateRenderArea(
        RenderArea area,
        TextureDescription description,
        GraphTextureRange range)
    {
        var mipWidth = description.Width;
        var mipHeight = description.Height;
        var mipEnd = range.FirstMip + range.MipCount;

        for (var mip = 0; mip < mipEnd; mip++)
        {
            if (mip >= range.FirstMip
                && ((area.Width > mipWidth)
                    || (area.X > (mipWidth - area.Width))
                    || (area.Height > mipHeight)
                    || (area.Y > (mipHeight - area.Height))))
            {
                throw new ArgumentException("The raster render area must fit every selected attachment mip.", nameof(range));
            }

            mipWidth = Math.Max(1, mipWidth / 2);
            mipHeight = Math.Max(1, mipHeight / 2);
        }
    }

    /// <summary>Adds one compute pass.</summary>
    /// <param name="description">The immutable pass ordering and predecessor declaration.</param>
    /// <returns>The graph-owned pass identity.</returns>
    public RenderPassId AddComputePass(RenderPassDescription description)
    {
        return AddPass(description, RenderGraphPassKind.Compute, callback: null);
    }

    /// <summary>Adds one compute pass with a scoped synchronous recorder callback.</summary>
    /// <param name="description">The immutable pass ordering and predecessor declaration.</param>
    /// <param name="callback">The graphics-owner callback that records this pass.</param>
    /// <returns>The graph-owned pass identity.</returns>
    public RenderPassId AddComputePass(RenderPassDescription description, RenderPassCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return AddPass(description, RenderGraphPassKind.Compute, callback);
    }

    /// <summary>Adds one copy pass.</summary>
    /// <param name="description">The immutable pass ordering and predecessor declaration.</param>
    /// <returns>The graph-owned pass identity.</returns>
    public RenderPassId AddCopyPass(RenderPassDescription description)
        => AddPass(description, RenderGraphPassKind.Copy, callback: null);

    /// <summary>Adds one copy pass with a scoped synchronous recorder callback.</summary>
    /// <param name="description">The immutable pass ordering and predecessor declaration.</param>
    /// <param name="callback">The graphics-owner callback that records this pass.</param>
    /// <returns>The graph-owned pass identity.</returns>
    public RenderPassId AddCopyPass(RenderPassDescription description, RenderPassCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return AddPass(description, RenderGraphPassKind.Copy, callback);
    }

    /// <summary>Adds one raster pass.</summary>
    /// <param name="description">The immutable pass ordering and predecessor declaration.</param>
    /// <returns>The graph-owned pass identity.</returns>
    public RenderPassId AddRasterPass(RenderPassDescription description)
        => AddPass(description, RenderGraphPassKind.Raster, callback: null);

    /// <summary>Adds one raster pass with a scoped synchronous recorder callback.</summary>
    /// <param name="description">The immutable pass ordering and predecessor declaration.</param>
    /// <param name="callback">The graphics-owner callback that records this pass.</param>
    /// <returns>The graph-owned pass identity.</returns>
    public RenderPassId AddRasterPass(RenderPassDescription description, RenderPassCallback callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return AddPass(description, RenderGraphPassKind.Raster, callback);
    }

    /// <summary>Compiles graph declarations into an immutable plan or exact rejection diagnostics.</summary>
    /// <returns>A compiled plan with no diagnostics, or diagnostics with no plan.</returns>
    public RenderGraphCompileResponse Compile()
    {
        if (_compiledResponse is not null)
        {
            return _compiledResponse;
        }

        _presentationSource ??= _defaultPresentationSource;
        var validation = _resources.ValidateCompilation();
        var presentationSourceValid = IsValidPresentationSource();

        if (!validation.IsValid || !presentationSourceValid)
        {
            return new RenderGraphCompileResponse(
                RenderGraphCompileStatus.Rejected,
                plan: null,
                CreateDiagnostics(validation, presentationSourceValid));
        }

        if (!_presentationPassId.HasValue)
        {
            var source = _presentationSource
                ?? throw new InvalidOperationException("The graph has no selected presentation source.");
            var range = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);

            if (_options.Surface is null)
            {
                _presentationPassId = _resources.AddOffscreenOutputSourcePass(source, range);
                _passKinds.Add(_presentationPassId.Value, RenderGraphPassKind.OffscreenOutput);
            }
            else
            {
                _presentationPassId = _resources.AddPresentationSourcePass(source, range);
                _passKinds.Add(_presentationPassId.Value, RenderGraphPassKind.Presentation);
            }

            _isSealed = true;
            validation = _resources.ValidateCompilation();
        }

        if (!validation.IsValid)
        {
            return new RenderGraphCompileResponse(
                RenderGraphCompileStatus.Rejected,
                plan: null,
                CreateDiagnostics(validation, presentationSourceValid));
        }

        var plan = new RenderGraphPlan(
            _options,
            _resources.DeviceEpoch,
            validation.OrderedPasses,
            GetOrderedPassKinds(validation.OrderedPasses),
            GetOrderedPassCallbacks(validation.OrderedPasses),
            _resources.Passes,
            validation.StateTransitions,
            _presentationSource
                ?? throw new InvalidOperationException("The graph has no selected presentation source."),
            _resources.Accesses,
            _resources.BufferImports,
            _resources.TextureImports,
            [.. _attachmentWrites],
            NonAliasableTransientBufferIds,
            NonAliasableTransientTextureIds,
            CreateBuiltInSpriteState(),
            _bufferReadbackExport);
        _compiledResponse = new RenderGraphCompileResponse(
            RenderGraphCompileStatus.Compiled,
            plan,
            [.. _buildWarnings]);
        _isSealed = true;
        return _compiledResponse;
    }

    /// <summary>Creates one transient buffer whose version 0 has invalid contents.</summary>
    /// <param name="description">The admitted buffer description.</param>
    /// <returns>The graph-owned version 0.</returns>
    public GraphBufferVersion CreateTransientBuffer(BufferDescription description)
    {
        EnsureCanMutate();
        return _resources.CreateTransientBuffer(description);
    }

    /// <summary>Creates one transient texture whose version 0 has invalid contents.</summary>
    /// <param name="description">The admitted texture description.</param>
    /// <returns>The graph-owned version 0.</returns>
    public GraphTextureVersion CreateTransientTexture(TextureDescription description)
    {
        EnsureCanMutate();
        return _resources.CreateTransientTexture(description);
    }

    /// <summary>Imports the exact selected buffer range and its actual owner state.</summary>
    /// <param name="buffer">The same-device live buffer.</param>
    /// <param name="range">The exact nonempty byte range.</param>
    /// <param name="options">The caller's actual state/content declaration and completion policy.</param>
    /// <returns>The graph-owned imported version 0.</returns>
    public GraphBufferVersion ImportBuffer(GpuBuffer buffer, GraphBufferRange range, GraphImportOptions options)
    {
        EnsureCanMutate();
        return _resources.ImportBuffer(buffer, range, options);
    }

    /// <summary>Imports the exact selected texture subresources and their actual owner state.</summary>
    /// <param name="texture">The same-device live texture.</param>
    /// <param name="range">The exact nonempty mip/layer/aspect range.</param>
    /// <param name="options">The caller's actual state/content declaration and completion policy.</param>
    /// <returns>The graph-owned imported version 0.</returns>
    public GraphTextureVersion ImportTexture(GpuTexture texture, GraphTextureRange range, GraphImportOptions options)
    {
        EnsureCanMutate();
        return _resources.ImportTexture(texture, range, options);
    }

    /// <summary>Declares a buffer read by one pass.</summary>
    /// <param name="pass">The graph-owned pass identity.</param>
    /// <param name="version">The graph-owned resource version.</param>
    /// <param name="usage">The typed read usage.</param>
    /// <param name="range">The exact accessed byte range.</param>
    public void ReadBuffer(RenderPassId pass, GraphBufferVersion version, GraphBufferUsage usage, GraphBufferRange range)
    {
        EnsureCanMutate();
        ValidateBufferUsage(pass, usage, isWrite: false);
        _resources.ReadBuffer(pass, version, range, usage);
    }

    /// <summary>Declares a texture read by one pass.</summary>
    /// <param name="pass">The graph-owned pass identity.</param>
    /// <param name="version">The graph-owned resource version.</param>
    /// <param name="usage">The typed read usage.</param>
    /// <param name="range">The exact accessed mip/layer/aspect range.</param>
    public void ReadTexture(RenderPassId pass, GraphTextureVersion version, GraphTextureUsage usage, GraphTextureRange range)
    {
        EnsureCanMutate();
        ValidateTextureUsage(pass, usage, isWrite: false);
        _resources.ReadTexture(pass, version, range, usage);
    }

    /// <summary>Declares a same-pass storage read of a buffer version and write to its next version.</summary>
    /// <param name="pass">The graph-owned raster or compute pass identity.</param>
    /// <param name="previousVersion">The graph-owned version read by the pass.</param>
    /// <param name="range">The exact byte range both read and written.</param>
    /// <returns>The next version written by the pass.</returns>
    /// <remarks>The prior range must be initialized. A shader using this result must declare <see cref="ShaderResourceAccess.ReadWrite"/>.</remarks>
    public GraphBufferVersion ReadWriteBuffer(
        RenderPassId pass,
        GraphBufferVersion previousVersion,
        GraphBufferRange range)
    {
        EnsureCanMutate();
        ValidateBufferUsage(pass, GraphBufferUsage.StorageRead, isWrite: false);
        ValidateBufferUsage(pass, GraphBufferUsage.StorageWrite, isWrite: true);
        return _resources.ReadWriteBuffer(pass, previousVersion, range);
    }

    /// <summary>Declares a same-pass storage read of a texture version and write to its next version.</summary>
    /// <param name="pass">The graph-owned raster or compute pass identity.</param>
    /// <param name="previousVersion">The graph-owned version read by the pass.</param>
    /// <param name="range">The exact mip/layer/aspect range both read and written.</param>
    /// <returns>The next version written by the pass.</returns>
    /// <remarks>The prior range must be initialized. A shader using this result must declare <see cref="ShaderResourceAccess.ReadWrite"/>.</remarks>
    public GraphTextureVersion ReadWriteTexture(
        RenderPassId pass,
        GraphTextureVersion previousVersion,
        GraphTextureRange range)
    {
        EnsureCanMutate();
        ValidateTextureUsage(pass, GraphTextureUsage.StorageRead, isWrite: false);
        ValidateTextureUsage(pass, GraphTextureUsage.StorageWrite, isWrite: true);
        return _resources.ReadWriteTexture(pass, previousVersion, range);
    }

    /// <summary>Selects the latest initialized mip-0/layer-0 color version for the graph's output target.</summary>
    /// <param name="version">The graph-owned version of the selected output target.</param>
    /// <exception cref="ArgumentException">The version does not satisfy the selected output target contract.</exception>
    /// <exception cref="InvalidOperationException">A presentation source was already selected.</exception>
    /// <exception cref="NotSupportedException">The Surface format has no implemented terminal output encoding.</exception>
    public void SetPresentationSource(GraphTextureVersion version)
    {
        EnsureCanMutate();
        ArgumentNullException.ThrowIfNull(version);

        if (_presentationSource is not null)
        {
            throw new InvalidOperationException("A presentation source was already selected.");
        }

        var requiredRange = new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color);

        if ((version.DeviceEpoch != _resources.DeviceEpoch)
            || (version.Range != requiredRange)
            || (version.Description.Dimension != TextureDimension.Dimension2D)
            || (version.Description.Width != _options.TargetExtent.Width)
            || (version.Description.Height != _options.TargetExtent.Height)
            || (version.Description.Format == TextureFormat.D32Float)
            || !_resources.IsTextureCurrentVersion(version))
        {
            throw new ArgumentException("The presentation source must be the current mip-0/layer-0 color version with the selected target extent.", nameof(version));
        }

        if (_options.Surface is null)
        {
            var target = _options.OffscreenColorTarget;

            if (((version.Description.Usage & TextureUsage.ColorAttachment) == 0)
                || (target is null)
                || !_resources.IsTextureImportedFrom(version, target))
            {
                throw new ArgumentException("The offscreen presentation source must be the selected color-attachment target.", nameof(version));
            }
        }
        else if (!SurfaceFormatCompatibility.TryGetSrgbPresentationSourceFormat(
            _options.Surface.ColorFormat,
            out var presentationSourceFormat))
        {
            throw new NotSupportedException("Surface output requires an sRGB-compatible color-attachment format.");
        }
        else if (((version.Description.Usage & TextureUsage.CopySource) == 0)
            || (version.Description.Format != presentationSourceFormat)
            || !IsSurfaceTargetCurrent())
        {
            throw new ArgumentException("A Surface presentation source must use the matching sRGB encoding format and active generation.", nameof(version));
        }

        _presentationSource = version;
    }

    /// <summary>Declares a storage or copy write and returns the next version.</summary>
    /// <param name="pass">The graph-owned pass identity.</param>
    /// <param name="previousVersion">The current graph-owned version.</param>
    /// <param name="usage">The typed write usage.</param>
    /// <param name="range">The exact written byte range.</param>
    /// <returns>The next version with only the written range initialized.</returns>
    public GraphBufferVersion WriteBuffer(
        RenderPassId pass,
        GraphBufferVersion previousVersion,
        GraphBufferUsage usage,
        GraphBufferRange range)
    {
        EnsureCanMutate();
        ValidateBufferUsage(pass, usage, isWrite: true);
        return _resources.WriteBuffer(pass, previousVersion, range, usage);
    }

    /// <summary>Declares a color-attachment write and returns the next version.</summary>
    /// <param name="pass">The raster pass identity.</param>
    /// <param name="previousVersion">The current graph-owned version.</param>
    /// <param name="range">The exact written mip/layer/aspect range.</param>
    /// <param name="loadAction">The attachment load action.</param>
    /// <param name="storeAction">The attachment store action.</param>
    /// <param name="clearColor">The finite linear clear color used by <see cref="AttachmentLoadAction.Clear"/>.</param>
    /// <returns>The next version with stored contents valid only for the written range.</returns>
    public GraphTextureVersion WriteColorAttachment(
        RenderPassId pass,
        GraphTextureVersion previousVersion,
        GraphTextureRange range,
        AttachmentLoadAction loadAction,
        AttachmentStoreAction storeAction,
        Vector4 clearColor)
    {
        EnsureCanMutate();
        ValidateRasterPass(pass);
        ArgumentNullException.ThrowIfNull(previousVersion);

        if (previousVersion.Description.Format is TextureFormat.D32Float)
        {
            throw new ArgumentException("A color attachment cannot use a depth format.", nameof(previousVersion));
        }

        if ((loadAction == AttachmentLoadAction.Clear) && !clearColor.IsFinite().All())
        {
            throw new ArgumentOutOfRangeException(nameof(clearColor), "A color attachment clear value must be finite.");
        }

        ValidateAttachmentRenderArea(pass, previousVersion, range, storeAction);
        var version = _resources.WriteAttachment(pass, previousVersion, range, loadAction, storeAction);
        _attachmentWrites.Add(new RenderGraphAttachmentWrite(
            pass,
            version,
            range,
            loadAction,
            storeAction,
            clearColor,
            ClearDepth: 0.0f,
            IsDepth: false));
        return version;
    }

    /// <summary>Declares a depth-attachment write and returns the next version.</summary>
    /// <param name="pass">The raster pass identity.</param>
    /// <param name="previousVersion">The current graph-owned version.</param>
    /// <param name="range">The exact written mip/layer/aspect range.</param>
    /// <param name="loadAction">The attachment load action.</param>
    /// <param name="storeAction">The attachment store action.</param>
    /// <param name="clearDepth">The finite reversed-Z depth clear value in [0, 1].</param>
    /// <returns>The next version with stored contents valid only for the written range.</returns>
    public GraphTextureVersion WriteDepthAttachment(
        RenderPassId pass,
        GraphTextureVersion previousVersion,
        GraphTextureRange range,
        AttachmentLoadAction loadAction,
        AttachmentStoreAction storeAction,
        float clearDepth)
    {
        EnsureCanMutate();
        ValidateRasterPass(pass);
        ArgumentNullException.ThrowIfNull(previousVersion);

        if (previousVersion.Description.Format is not TextureFormat.D32Float)
        {
            throw new ArgumentException("A depth attachment requires the D32Float format.", nameof(previousVersion));
        }

        if ((loadAction == AttachmentLoadAction.Clear)
            && (!float.IsFinite(clearDepth) || (clearDepth < 0.0f) || (clearDepth > 1.0f)))
        {
            throw new ArgumentOutOfRangeException(nameof(clearDepth), "A depth clear value must be finite and in [0, 1].");
        }

        ValidateAttachmentRenderArea(pass, previousVersion, range, storeAction);
        var version = _resources.WriteAttachment(pass, previousVersion, range, loadAction, storeAction);
        _attachmentWrites.Add(new RenderGraphAttachmentWrite(
            pass,
            version,
            range,
            loadAction,
            storeAction,
            default,
            clearDepth,
            IsDepth: true));
        return version;
    }

    /// <summary>Declares a storage or copy write and returns the next version.</summary>
    /// <param name="pass">The graph-owned pass identity.</param>
    /// <param name="previousVersion">The current graph-owned version.</param>
    /// <param name="usage">The typed write usage.</param>
    /// <param name="range">The exact written mip/layer/aspect range.</param>
    /// <returns>The next version with only the written range initialized.</returns>
    public GraphTextureVersion WriteTexture(
        RenderPassId pass,
        GraphTextureVersion previousVersion,
        GraphTextureUsage usage,
        GraphTextureRange range)
    {
        EnsureCanMutate();
        ValidateTextureUsage(pass, usage, isWrite: true);
        return _resources.WriteTexture(pass, previousVersion, range, usage);
    }

    internal void AddBuildWarning(string code, string message)
    {
        EnsureCanMutate();
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        _buildWarnings.Add(CreateDiagnostic(
            code,
            message,
            DiagnosticSeverity.Warning,
            "Renderer.BuildGraph"));
    }

    internal RenderPassId AddBuiltInMeshNormalDiagnosticComputePass(RenderPassDescription description)
    {
        if (_builtInMeshNormalDiagnosticPassId.HasValue)
        {
            throw new InvalidOperationException("The render graph already has a built-in Mesh diagnostic pass.");
        }

        var pass = AddPass(description, RenderGraphPassKind.Compute, callback: null);
        _builtInMeshNormalDiagnosticPassId = pass;
        return pass;
    }

    internal RenderPassId AddBuiltInMeshRasterPass(RenderPassDescription description, RenderMeshC0GraphPass meshPass)
    {
        ArgumentNullException.ThrowIfNull(meshPass);
        var pass = AddPass(description, RenderGraphPassKind.Raster, callback: null);
        _builtInMeshPasses.Add(pass, meshPass);
        return pass;
    }

    internal RenderPassId AddBuiltInMeshShadowRasterPass(
        RenderPassDescription description,
        RenderMeshC0ShadowGraphPass meshPass)
    {
        ArgumentNullException.ThrowIfNull(meshPass);
        var pass = AddPass(description, RenderGraphPassKind.Raster, callback: null);
        _builtInMeshShadowPasses.Add(pass, meshPass);
        return pass;
    }

    internal RenderPassId AddBuiltInSpriteRasterPass(RenderPassDescription description, RenderSpriteC0GraphPass spritePass)
    {
        ArgumentNullException.ThrowIfNull(spritePass);
        var pass = AddPass(description, RenderGraphPassKind.Raster, callback: null);
        _builtInSpritePasses.Add(pass, spritePass);
        return pass;
    }

    internal RenderPassId AddBuiltInSpriteToneMapRasterPass(
        RenderPassDescription description,
        RenderSpriteToneMapGraphPass toneMapPass)
    {
        ArgumentNullException.ThrowIfNull(toneMapPass);
        var pass = AddPass(description, RenderGraphPassKind.Raster, callback: null);
        _builtInSpriteToneMapPasses.Add(pass, toneMapPass);
        return pass;
    }

    internal GraphTextureVersion CreateDeferredMeshTexture(
        TextureDescription description,
        int drawIndex,
        int textureAssetId)
    {
        EnsureCanMutate();
        ArgumentNullException.ThrowIfNull(description);
        ArgumentOutOfRangeException.ThrowIfNegative(drawIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureAssetId);

        if ((description.Usage & TextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("A deferred Mesh texture must support sampling.", nameof(description));
        }

        var version = _resources.CreateTransientTexture(
            description,
            contentsValid: true,
            GraphResourceState.General);
        _builtInMeshDeferredTextures.Add(new RenderMeshGraphDeferredTexture(version, drawIndex, textureAssetId));
        _ = _nonAliasableTransientTextureIds.Add(version.ResourceId);
        return version;
    }

    internal GraphTextureVersion CreateDeferredTransientTexture(TextureDescription description, int drawIndex)
    {
        EnsureCanMutate();
        ArgumentNullException.ThrowIfNull(description);
        ArgumentOutOfRangeException.ThrowIfNegative(drawIndex);

        if ((description.Usage & TextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("A deferred Sprite texture must support sampling.", nameof(description));
        }

        var version = _resources.CreateTransientTexture(
            description,
            contentsValid: true,
            GraphResourceState.General);
        _builtInSpriteDeferredTextures.Add(new RenderSpriteGraphDeferredTexture(version, drawIndex));
        _ = _nonAliasableTransientTextureIds.Add(version.ResourceId);
        return version;
    }

    internal GraphBufferVersion CreateInitializedTransientBuffer(BufferDescription description, byte[] initialContents)
    {
        EnsureCanMutate();
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(initialContents);

        if ((description.Usage & BufferUsage.CopyDestination) == 0
            || (description.MemoryKind != GpuMemoryKind.DeviceLocal)
            || (initialContents.Length != description.ByteLength))
        {
            throw new ArgumentException("Built-in upload buffers require exact device-local copy-destination contents.", nameof(description));
        }

        var version = _resources.CreateTransientBuffer(
            description,
            contentsValid: true,
            GraphResourceState.General);
        _builtInSpriteBufferUploads.Add(new RenderSpriteGraphBufferUpload(version, initialContents));
        // Built-in uploads complete before graph execution, so these buffers cannot safely share transient backing.
        _ = _nonAliasableTransientBufferIds.Add(version.ResourceId);
        return version;
    }

    internal void ExportBufferForReadback(GraphBufferVersion version, GraphBufferRange range)
    {
        EnsureCanMutate();
        ArgumentNullException.ThrowIfNull(version);
        _resources.ValidateTransientBufferVersion(version);

        if (version.DeviceEpoch != _resources.DeviceEpoch
            || !range.IsWithin(new GraphBufferRange(0, version.Description.ByteLength)))
        {
            throw new ArgumentException("A readback export must name a valid range on this graph's device.", nameof(version));
        }

        if (_bufferReadbackExport.HasValue)
        {
            throw new InvalidOperationException("The graph already has a buffer readback export.");
        }

        _bufferReadbackExport = new GraphBufferReadbackExport(version, range);
        _ = _nonAliasableTransientBufferIds.Add(version.ResourceId);
    }

    internal GraphTextureVersion GetBuiltInOutputColorVersion()
    {
        EnsureCanMutate();

        if (_offscreenColorTargetVersion is GraphTextureVersion offscreenVersion)
        {
            return offscreenVersion;
        }

        if (_builtInSurfaceColorTargetVersion is GraphTextureVersion surfaceVersion)
        {
            return surfaceVersion;
        }

        var surface = _options.Surface
            ?? throw new InvalidOperationException("The graph has no built-in output target.");
        _builtInSurfaceColorTargetVersion = _resources.CreateTransientTexture(
            new TextureDescription(
                TextureDimension.Dimension2D,
                _options.TargetExtent.Width,
                _options.TargetExtent.Height,
                depth: 1,
                arrayLayers: 1,
                mipLevels: 1,
                surface.PresentationSourceFormat,
                TextureUsage.ColorAttachment | TextureUsage.CopySource,
                "sprite-presentation-source"));
        return _builtInSurfaceColorTargetVersion;
    }

    internal void SetBuiltInMeshNormalDiagnosticComputePass(
        RenderPassId pass,
        RenderMeshC0NormalDiagnosticGraphPass meshPass)
    {
        if (_builtInMeshNormalDiagnosticPassId != pass
            || _builtInMeshNormalDiagnosticPass.HasValue
            || (meshPass.DrawCount <= 0))
        {
            throw new InvalidOperationException("The built-in Mesh diagnostic pass state is missing, duplicated, or invalid.");
        }

        _builtInMeshNormalDiagnosticPass = meshPass;
    }

    internal void SetDefaultPresentationSource(GraphTextureVersion version)
    {
        EnsureCanMutate();
        ArgumentNullException.ThrowIfNull(version);

        if (_defaultPresentationSource is not null)
        {
            throw new InvalidOperationException("The graph already has a built-in presentation source.");
        }

        _defaultPresentationSource = version;
    }

    private RenderPassId AddPass(
        RenderPassDescription description,
        RenderGraphPassKind passKind,
        RenderPassCallback? callback)
    {
        EnsureCanMutate();
        ArgumentNullException.ThrowIfNull(description);

        if (!Enum.IsDefined(passKind)
            || (passKind is RenderGraphPassKind.Presentation or RenderGraphPassKind.OffscreenOutput))
        {
            throw new ArgumentOutOfRangeException(nameof(passKind));
        }

        if (description.RenderArea is RenderArea renderArea)
        {
            if (passKind is not RenderGraphPassKind.Raster)
            {
                throw new ArgumentException("Only raster passes may declare a render area.", nameof(description));
            }
        }

        var passId = _resources.AddPass(description);
        _passKinds.Add(passId, passKind);
        _passRenderAreas.Add(passId, description.RenderArea);

        if (callback is not null)
        {
            _passCallbacks.Add(passId, callback);
        }

        return passId;
    }

    private RenderSpriteGraphState? CreateBuiltInSpriteState()
    {
        if (_builtInSpritePasses.Count == 0
            && _builtInMeshPasses.Count == 0
            && _builtInMeshShadowPasses.Count == 0
            && !_builtInMeshNormalDiagnosticPassId.HasValue
            && _builtInSpriteToneMapPasses.Count == 0
            && _builtInSpriteBufferUploads.Count == 0
            && _builtInSpriteDeferredTextures.Count == 0
            && _builtInMeshDeferredTextures.Count == 0)
        {
            return null;
        }

        var targetFormat = _options.Surface?.PresentationSourceFormat
            ?? _options.OffscreenColorTarget?.Description.Format
            ?? throw new InvalidOperationException("A built-in Sprite graph has no output format.");
        return new RenderSpriteGraphState(
            TextureFormat.Rgba16Float,
            targetFormat,
            [.. _builtInSpriteBufferUploads],
            [.. _builtInSpriteDeferredTextures],
            [.. _builtInMeshDeferredTextures],
            _builtInSpritePasses.ToImmutableDictionary(),
            _builtInMeshPasses.ToImmutableDictionary(),
            _builtInMeshShadowPasses.ToImmutableDictionary(),
            _builtInSpriteToneMapPasses.ToImmutableDictionary(),
            _builtInMeshNormalDiagnosticPassId,
            _builtInMeshNormalDiagnosticPass);
    }

    private Diagnostic CreateDiagnostic(
        string code,
        string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Error,
        string operation = "RenderGraph.Compile")
        => new(
            code,
            severity,
            Stopwatch.GetTimestamp(),
            hostId: null,
            worldGuid: _options.WorldGuid,
            deviceId: null,
            surfaceId: null,
            deviceEpoch: _resources.DeviceEpoch,
            surfaceEpoch: null,
            swapchainEpoch: null,
            tick: _options.TickIndex,
            systemId: null,
            passId: null,
            assetId: null,
            commandIdentity: null,
            operation,
            nativeResult: null,
            message: message,
            context: []);

    private ImmutableArray<Diagnostic> CreateDiagnostics(GraphResourceValidationResult validation, bool presentationSourceValid)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>(5 + _buildWarnings.Count);
        diagnostics.AddRange(_buildWarnings);

        if (!validation.ImportsValid)
        {
            diagnostics.Add(CreateDiagnostic(ImportInvalidCode, "An imported range no longer matches its captured owner state, contents, completion policy, or nonoverlap requirement."));
        }

        if (!validation.AccessesValid)
        {
            diagnostics.Add(CreateDiagnostic(AccessInvalidCode, "The graph contains overlapping or incompatible accesses within one pass."));
        }

        if (!validation.ContentsValid)
        {
            diagnostics.Add(CreateDiagnostic(UninitializedReadCode, "A graph read observes contents that were not initialized for the exact resource range."));
        }

        if (!validation.DependenciesValid)
        {
            diagnostics.Add(CreateDiagnostic(DependencyCycleCode, "The declared and resource-derived pass dependencies contain a cycle."));
        }

        if (!presentationSourceValid)
        {
            var targetName = (_options.Surface is null) ? "offscreen target" : "interactive Surface";
            diagnostics.Add(CreateDiagnostic(PresentationSourceInvalidCode, $"The graph has no initialized final color version for the selected {targetName}."));
        }

        return diagnostics.ToImmutable();
    }

    private void EnsureCanMutate()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A successfully compiled render graph is immutable.");
        }
    }

    private ImmutableArray<RenderPassCallback?> GetOrderedPassCallbacks(ImmutableArray<RenderPassId> orderedPasses)
    {
        var callbacks = ImmutableArray.CreateBuilder<RenderPassCallback?>(orderedPasses.Length);

        foreach (var pass in orderedPasses)
        {
            callbacks.Add(_passCallbacks.TryGetValue(pass, out var callback) ? callback : null);
        }

        return callbacks.MoveToImmutable();
    }

    private ImmutableArray<RenderGraphPassKind> GetOrderedPassKinds(ImmutableArray<RenderPassId> orderedPasses)
    {
        var passKinds = ImmutableArray.CreateBuilder<RenderGraphPassKind>(orderedPasses.Length);

        foreach (var pass in orderedPasses)
        {
            passKinds.Add(GetPassKind(pass));
        }

        return passKinds.MoveToImmutable();
    }

    private RenderGraphPassKind GetPassKind(RenderPassId pass)
    {
        if (_passKinds.TryGetValue(pass, out var passKind))
        {
            return passKind;
        }

        throw new ArgumentException("The pass ID is not registered in this graph.", nameof(pass));
    }

    private bool IsSurfaceTargetCurrent()
    {
        var surface = _options.Surface;

        if ((surface is null)
            || !surface.IsLive
            || (surface.Device.DeviceEpoch != _options.DeviceEpoch)
            || (surface.Device.Status != GraphicsDeviceStatus.Ready))
        {
            return false;
        }

        var current = surface.CaptureSnapshot();
        return (current.Capabilities.SurfaceEpoch == _options.SurfaceEpoch)
            && (current.Capabilities.SwapchainEpoch == _options.SwapchainEpoch)
            && (current.Capabilities.SwapchainEpoch != 0)
            && (current.ClientPixelSize == _options.TargetExtent);
    }

    private bool IsValidPresentationSource()
    {
        if ((_presentationSource is not GraphTextureVersion version)
            || (version.DeviceEpoch != _resources.DeviceEpoch)
            || (version.Range != new GraphTextureRange(0, 1, 0, 1, TextureAspect.Color))
            || (version.Description.Dimension != TextureDimension.Dimension2D)
            || (version.Description.Width != _options.TargetExtent.Width)
            || (version.Description.Height != _options.TargetExtent.Height)
            || (version.Description.Format == TextureFormat.D32Float)
            || !_resources.IsTextureCurrentVersion(version)
            || !_resources.IsTextureInitialized(version, version.Range))
        {
            return false;
        }

        if (_options.Surface is not null)
        {
            return SurfaceFormatCompatibility.TryGetSrgbPresentationSourceFormat(
                    _options.Surface.ColorFormat,
                    out var presentationSourceFormat)
                && ((version.Description.Usage & TextureUsage.CopySource) != 0)
                && (version.Description.Format == presentationSourceFormat)
                && IsSurfaceTargetCurrent();
        }

        var target = _options.OffscreenColorTarget;
        return ((version.Description.Usage & TextureUsage.ColorAttachment) != 0)
            && (target is not null)
            && _resources.IsTextureImportedFrom(version, target);
    }

    private void ValidateAttachmentRenderArea(
        RenderPassId pass,
        GraphTextureVersion previousVersion,
        GraphTextureRange range,
        AttachmentStoreAction storeAction)
    {
        if (!_passRenderAreas.TryGetValue(pass, out var renderArea)
            || !range.IsWithin(previousVersion.Range))
        {
            return;
        }

        var area = renderArea
            ?? new RenderArea(0, 0, _options.TargetExtent.Width, _options.TargetExtent.Height);
        ValidateRenderArea(area, previousVersion.Description, range);

        if ((storeAction is AttachmentStoreAction.Store)
            && !IsFullRenderArea(area, previousVersion.Description, range)
            && !_resources.IsTextureInitialized(previousVersion, range))
        {
            throw new ArgumentException(
                "A stored partial-area attachment write requires valid prior contents for the selected subresources.",
                nameof(previousVersion));
        }
    }

    private void ValidateBufferUsage(RenderPassId pass, GraphBufferUsage usage, bool isWrite)
    {
        var passKind = GetPassKind(pass);
        var valid = passKind switch {
            RenderGraphPassKind.Copy => isWrite
                ? (usage == GraphBufferUsage.CopyDestination)
                : (usage == GraphBufferUsage.CopySource),
            RenderGraphPassKind.Raster => usage is not (GraphBufferUsage.CopySource or GraphBufferUsage.CopyDestination),
            RenderGraphPassKind.Compute => usage is not (GraphBufferUsage.VertexRead or GraphBufferUsage.IndexRead or GraphBufferUsage.CopySource or GraphBufferUsage.CopyDestination),
            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException("The buffer usage is not legal for the selected pass kind.", nameof(usage));
        }
    }

    private void ValidateRasterPass(RenderPassId pass)
    {
        if (GetPassKind(pass) != RenderGraphPassKind.Raster)
        {
            throw new ArgumentException("Attachment writes require a raster pass.", nameof(pass));
        }
    }

    private void ValidateTextureUsage(RenderPassId pass, GraphTextureUsage usage, bool isWrite)
    {
        var passKind = GetPassKind(pass);
        var valid = passKind switch {
            RenderGraphPassKind.Copy => isWrite
                ? (usage == GraphTextureUsage.CopyDestination)
                : (usage == GraphTextureUsage.CopySource),
            RenderGraphPassKind.Raster or RenderGraphPassKind.Compute => usage is not (GraphTextureUsage.CopySource or GraphTextureUsage.CopyDestination),
            _ => false,
        };

        if (!valid)
        {
            throw new ArgumentException("The texture usage is not legal for the selected pass kind.", nameof(usage));
        }
    }
}
