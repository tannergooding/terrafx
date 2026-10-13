# Public API consumer guide (informative)

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

This chapter connects the public declarations to typical caller workflows. It adds no signatures, defaults, record fields, or behavioral requirements. [30](30-public-api-review-surface.md) is the declaration baseline; [02](02-dotnet-api-memory-and-ownership.md) owns global API policy. Required integration witnesses are in [32](32-end-to-end-integration-scenarios.md), not abbreviated examples here.

## 1. Host, world, and component use

Reference the `TerraFX.Runtime` library package for the integrated engine and one explicitly selected D3D12/Vulkan backend package for native graphics. A headless application needs no backend package. Core/Assets/Input/Platform/Graphics/Physics/Audio arrive transitively; do not add packages for the merged Entities/Scenes/Animation/Rendering/UI/Integration/Hosting namespaces. Public subsystem `using` directives stay semantic; shared foundation imports are `using TerraFX;`/`using TerraFX.Buffers;`, not the assembly name. Independently reusable services instead reference their owning library; standalone Physics requires only Core. [29](29-project-setup-build-and-deployment.md#library-packages-and-consumer-references) defines the full consumer table and the deliberate difference between integrated restore closure and selected published assets.

Package references do not start or register every contained service. Supply selected typed modules/features/closed generics and keep initialization scoped to the admitted host profile. The one-assembly package rule is not a claim that native dependencies trim automatically, or that a namespace's code is absent merely because its service is disabled.

Start the selected profile with `EngineHost.StartAsync`. Use `InvokeAsync` for a synchronous owner-thread setup callback: create the world, register component schemas/systems, and establish scene/input integration before registration freezes. An awaited factory does not transfer its native owner thread to the caller.

At an idle owner boundary, direct entity creation/destruction is available; during a tick, use the admitted `CommandWriter`. Chunk traversal passes a local ordinary/ref-struct callable by `ref`, preserving its state while borrowing scoped views; copied entity/component values can outlive an invocation. Generated query/column registrations make the same flow available in NativeAOT without reflection-only activation. `TryGet<T>` distinguishes a missing component from invalid ownership. [Chapter 12](12-entities-components-and-queries.md) defines storage/mutation and [Chapter 13](13-jobs-and-frame-pipeline.md) defines ordering/publication.

Select options from their owning contracts rather than copying another scenario's defaults: [04](04-capacity-and-workload-profiles.md) for shared capacities, [07](07-spatial-coordinates-and-indexing.md) for registered spatial settings, and [15](15-platform-host-and-devices.md) for host profiles/lifecycle. Observe `Completion` for host termination and use the declared shutdown operations rather than running or joining its pump on the caller thread.

Clock use follows [03](03-shared-conventions-and-clocks.md#2-four-clocks), without a caller-owned `Stopwatch`/`TimeProvider`. [02's boundary policy](02-dotnet-api-memory-and-ownership.md#public-type-boundary) distinguishes reusable foundational values/protocols, including `ImmutableArray<T>`, from private backing objects. Physics restore maps support read-only lookup without promising a particular dictionary implementation.

[02's signed integer default](02-dotnet-api-memory-and-ownership.md#integer-types-and-boundaries) lets ordinary `int` span lengths flow into `nint` CPU-buffer operations; buffer chains and other logical extents use `long`. GPU/native/wire spelling does not dictate public signedness. Validate domain limits before narrowing; an unsigned identity or mask is not an arithmetic index. The unchanged binary layouts have explicit checked semantic conversions in [10](10-binary-data-and-authoring-formats.md), not CLR object dumps.

## 2. Assets, scenes, and rendering

Use [Chapter 06](06-owned-geometry-and-numerics-interchange.md)'s `TerraFX.Numerics` values and explicit identity pose construction. Forward members such as `AsSystemVector2` copy bits to the corresponding BCL value; they do not convert an imported basis or projection convention or expose a borrowed alias. The reverse extensions, such as `systemValue.AsTerraFXVector2()`, are declared by `TerraFX.Numerics.SystemNumericsExtensions` in [30](30-public-api-review-surface.md#geometry). Import that namespace or invoke the extension class statically with full qualification; use aliases when BCL/engine type names collide. Both directions remain safe-callable from a consumer with unsafe compilation disabled. Reviewed bitcasts use narrowly scoped unsafe implementation blocks rather than propagating a caller obligation.

Load an `AssetLease<T>` for CPU readiness, then request GPU residency separately when needed. Prepare a scene asynchronously and activate it at the owner's `Begin` transaction. Unload releases scene ownership without revoking independently retained render/audio versions. [11](11-assets-import-streaming-and-packages.md) and [16](16-scenes-transforms-and-cameras.md) define those boundaries.

`value.IsFinite()` retains one mask lane per component; use `value.IsFinite().All()` to establish complete finite admission, or `Any()`/`None()` for those explicit queries. Parameterless reductions match complete all-bits-set lanes; `value.All(0)` instead compares every numeric component with zero using IEEE equality. Quaternion classification returns `Vector4`; matrices retain their mask rows. Do not compare a true float mask numerically to NaN or include an imaginary fourth Vector3 lane. Ordinary `Equals` and `Try` results remain scalar.

Create millimeter settings as `new SpatialSettings(-3, 12)`, with a matching explicitly registered profile identity. The exponent defines exact meters per unit; it is not a request to materialize `MathF.Pow(10, -3)` as the authoritative scale. Use the registered conversion/domain operations, which apply dimensional powers and destination rounding; live unit changes remain explicit profile/content conversions. [25](25-ui-text-and-editor-tools.md#spatial-unit-controls)'s named presets improve authoring UX without changing the raw contract.

The render flow is `Extract` -> `BuildGraph` -> optional custom passes -> `Compile` -> `SubmitAsync`. Interactive output uses the owner-acquired `EngineHost.GetSurface`; offscreen output uses a real same-device texture. Headless extraction remains CPU-only. Pass IDs identify passes; write declarations return resource versions. A recorder binds a created pipeline and accesses declared ranges, not a live world.

Observe submission completion separately from interactive presentation. Upload/readback operations have their own retained-input/output lifetimes. [17](17-graphics-device-and-resources.md), [19](19-render-graph-synchronization-and-presentation.md), and [21](21-2d-and-3d-rendering.md) own the contracts; [32 sections 4-5](32-end-to-end-integration-scenarios.md#4-graph-resourceuploadreadback) supplies required native witnesses.

## 3. Standalone subsystems

Standalone `PhysicsWorld2D`/`PhysicsWorld3D` use an owner context and dimensional options, typed commands, structural preparation/commit, fixed ticks, and copied results. Local physics success is not a committed ECS tick; `TerraFX.Integration` provides that bridge. [22](22-custom-physics-world-and-solver.md) and [23](23-custom-physics-shapes-queries-and-ccd.md) define faults, queries, rebase, and checkpoints. Span queries collect all output; constrained generic hit visitors borrow mutable local state by `ref` and distinguish complete delivery from `VisitorStopped` without bypassing geometric validation.

Select `AudioOutputMode.Native`, `Offline`, or `NotRequested` explicitly. Applied playback commands, voice end, and actual device output are different observations. [24](24-custom-audio-mixing-and-spatialization.md) defines endpoint and offline-consumer behavior.

Await PlayAsync/StopAsync/StopAllAsync/SetBusGainAsync/SetBusCutoffAsync/ReplaceBusGraphAsync; command kind names are not synchronous method aliases. TryGetBusHandle returns a value handle directly and invalid default on false. TrySubmit's nullable Task output has a bool-flow annotation; codec Read's enum status cannot provide that inference. Check status and reference nullability separately, according to the admitted schema.

Each `InputRouter` serves one UI or simulation-timeline recipient. `BindGameplayInput` establishes the window/world route; headless consumers inject normalized records. UI text/focus and physical gameplay actions are different products. Their owners are [14](14-custom-input-actions-and-text.md) and [25](25-ui-text-and-editor-tools.md).

Editor-facing degrees, length units, map bearings and canvas coordinates are [25's authoring presentations](25-ui-text-and-editor-tools.md#authoring-presentation-and-conversion), not alternate runtime API semantics. Typed registrations retain quantity/frame/reference context and canonical values; display preferences neither change a frozen spatial profile nor assign rounded text back to untouched fields. [10](10-binary-data-and-authoring-formats.md) owns the separate source-adapter/cooking boundary.

## 4. Ownership navigation

Shared callables use `using TerraFX;`: window actions implement IAction<ReadOnlySpan<T>>/IAction<Span<T>>, chain visitors IFunc<ReadOnlySpan<T>, bool>, chunk actions IAction<EntityChunkView> and physics visitors IReadOnlyRefFunc<PhysicsHit2D/3D, bool>. Pass the concrete local by ref; do not store it as an interface/delegate or change physics to by-value hits.

Recurring managed scratch uses `using TerraFX.Pooling;` and one owner-thread ObjectPool<T>. Construct with bounded capacity/policy, Prewarm before the loop, Rent/TryRent, Use with an IAction<T>, and Return only after actual consumers finish. Rent does not create; token copies do not extend ownership. Close/Dispose stops admission, then return outstanding rentals and service any explicit cleanup failures until IsDrained. This is not the GPU retirement protocol.

For authored construction, `Angle.FromDegrees(90)` retains degrees; `Matrix3x2.CreateRotation(angle)` evaluates the definition directly. Same-unit arithmetic stays in that unit, mixed-unit arithmetic requires an explicit conversion, and finite evaluation/overflow are checked. Existing float-radian fields and SI angular velocities remain. Source-only degree/turn/radian bindings survive save through the editor/converter path, not raw Angle serialization.

The first-sprite/model path uses existing scene components and Renderer defaults, influenced by modern DirectXTK12 as well as XNA. Follow CPU lease -> GPU residency -> owner-boundary setup -> Extract/BuildGraph/Compile/SubmitAsync. Do not construct backend allocator/descriptor helpers or interpret CPU Draw/End as GPU completion.

| Caller concern | Contract to consult |
|---|---|
| Copied handles, components, and scoped chunk views | [12](12-entities-components-and-queries.md) |
| Tick context, command receipts, and committed publications | [13](13-jobs-and-frame-pipeline.md) |
| Retained bytes, generic window visitors and read-only finalized outputs | [05](05-owned-buffers-and-internal-collections.md) |
| CPU asset versions and dependent leases | [11](11-assets-import-streaming-and-packages.md) |
| Immutable draw snapshots and GPU/presentation retirement | [21](21-2d-and-3d-rendering.md), [19](19-render-graph-synchronization-and-presentation.md) |
| Physics, audio, and input observations | [22](22-custom-physics-world-and-solver.md), [24](24-custom-audio-mixing-and-spatialization.md), [14](14-custom-input-actions-and-text.md) |
| Diagnostics and durable save state | [28](28-diagnostics-performance-and-validation.md), [27](27-state-serialization-replay-and-recovery.md) |

## 5. Implementation

Runtime, editor and executable tools use closed AOT registrations. Data/shader reload remains a live boundary operation; managed-code/registry changes require [26](26-csharp-systems-plugins-and-hot-reload.md)'s explicit build/save/shutdown/relaunch flow rather than a JIT-only module mode.

[Chapter 32](32-end-to-end-integration-scenarios.md) defines complete headless/2D/3D/editor/native-failure flows, [Chapter 34](34-engine-acceptance-cases.md) defines cross-engine observations, and [35](35-implementation-roadmap.md) defines delivery prerequisites. AR01/AR02 in [30](30-public-api-review-surface.md#11-api-acceptance-obligations) require separate compiled public consumers and exact constructor/default behavior. This guide is not evidence that those consumers have executed.

## 6. Complete selected caller compilation units

Each fence below is one complete source file illustrating selected contracts from [30](30-public-api-review-surface.md). Compile these consumers against the implemented surface without invented support types or throwing-body projections standing in for implementation. The complete headless/first-sprite/model/editor and standalone physics/audio programs remain mandatory AR01 consumers of their transitive supporting-type catalogues, built with their owning features under [35](35-implementation-roadmap.md); these smaller files do not substitute for those integration programs or establish runtime/AOT behavior.

### Geometry and narrow generic operators

```csharp
using System;
using System.Globalization;
using System.Numerics;
using TerraFX.Numerics;
using EngineVector2 = TerraFX.Numerics.Vector2;

public static class GeometryConsumer
{
    public static T Add<T>(T left, T right) where T : IAdditionOperators<T, T, T>
    {
        return left + right;
    }

    public static EngineVector2 Direction(Angle angle)
    {
        var (sin, cos) = angle.SinCos();
        return new EngineVector2(cos, sin);
    }

    public static bool FormatDefinition(Angle angle, Span<byte> destination, out int written)
    {
        return angle.TryFormat(destination, out written, "G9", CultureInfo.InvariantCulture);
    }

    public static EngineVector2 RoundTrip(System.Numerics.Vector2 source)
    {
        EngineVector2 value = source.AsTerraFXVector2();
        return value.AsSystemVector2().AsTerraFXVector2();
    }
}
```

### Scoped byte windows and bounded managed scratch

Main is a complete Core-only program. It copies input before borrowing, releases the read lease before its allocation, and returns a rental in finally before pool disposal. The managed policy owns no native resource; its ordinary-class action omits a by-value scoped modifier. It does not represent asynchronous use or GPU retirement.

```csharp
using System;
using TerraFX;
using TerraFX.Buffers;
using TerraFX.Pooling;

public static class CoreConsumer
{
    public static void Main()
    {
        using UnmanagedBuffer<byte> buffer = UnmanagedBuffer<byte>.Allocate(
            length: 4, options: new BufferAllocationOptions(maximumAllocationBytes: 256, alignment: 16));
        buffer.CopyFrom(new byte[] { 1, 2, 3, 4 }, destinationOffset: 0);
        using BufferReadLease<byte> read = buffer.AcquireRead(offset: 0, length: 4);
        ByteSum sum = default;
        read.UseWindow(relativeOffset: 0, length: 4, action: ref sum);

        using ObjectPool<byte[]> pool = new(maximumCapacity: 2, policy: new ScratchPolicy());
        pool.Prewarm(availableCount: 2);
        PoolRental<byte[]> rental = pool.Rent();
        try
        {
            FillScratch fill = new() { Value = checked((byte)sum.Total) };
            pool.Use(rental, ref fill);
        }
        finally
        {
            pool.Return(rental);
        }

        Console.WriteLine(sum.Total);
    }

    private struct ByteSum : IAction<ReadOnlySpan<byte>>
    {
        public long Total;

        public void Invoke(scoped ReadOnlySpan<byte> argument)
        {
            foreach (byte value in argument)
            {
                Total = checked(Total + value);
            }
        }
    }

    private struct FillScratch : IAction<byte[]>
    {
        public byte Value;

        public void Invoke(byte[] argument)
        {
            argument.AsSpan().Fill(Value);
        }
    }

    private sealed class ScratchPolicy : IPoolPolicy<byte[]>
    {
        public byte[] Create()
        {
            return new byte[64];
        }

        public void Reset(byte[] value)
        {
            Array.Clear(value);
        }

        public void Destroy(byte[] value)
        {
            Array.Clear(value);
        }
    }
}
```

### Nonnullable reference-schema codec consumption

This application helper consumes exactly one complete nonnullable reference record. Other callers may buffer NeedMoreData or accept schema-authorized null; this helper deliberately does neither. The explicit null guard is necessary even after Complete.

```csharp
using System;
using System.IO;
using TerraFX.Assets;

public static class CodecConsumer
{
    public static T ReadComplete<T>(IBinarySchemaCodec<T> codec, ReadOnlySpan<byte> source)
        where T : class
    {
        BinaryRecordStatus status = codec.Read(source, out T? value, out int consumed);
        if (status != BinaryRecordStatus.Complete)
        {
            throw new InvalidDataException($"Expected a complete record; got {status}.");
        }
        if (value is null)
        {
            throw new InvalidOperationException("The codec violated its nonnullable reference schema.");
        }
        if (consumed != source.Length)
        {
            throw new InvalidDataException("Expected exactly one record with no trailing bytes.");
        }

        return value;
    }
}
```

### Standalone async-owner scope

Factory cancellation is separate from eventual owner cleanup. DisposeAsync receives no cancellation token: once created, the owner must reach actual shutdown even when the operation is canceled. Each invocation's ValueTask is awaited once; applications cannot repeatedly consume a cached disposal ValueTask.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using TerraFX.Assets;

public static class AssetOwnerConsumer
{
    public static async Task RunAsync(string absoluteCacheDirectory,
        Func<AssetStore, CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using AssetStore store = await AssetStore.CreateAsync(
            new AssetStoreOptions(absoluteCacheDirectory), cancellationToken).ConfigureAwait(false);
        await operation(store, cancellationToken).ConfigureAwait(false);
    }
}
```
