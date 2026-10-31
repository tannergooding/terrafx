// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Numerics;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneComponentDefinitionTests
{
    [Test]
    public void BuiltInSceneComponentsUseWorldDefaultsAndMutationValidation()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        var registration = builder.Build();
        var transformRegistration = registration.TryGetComponentType<SceneComponentDefinitions.LocalTransformComponent>(out var componentRegistration)
            ? componentRegistration ?? throw new InvalidOperationException("The LocalTransform registration was null.")
            : throw new InvalidOperationException("The LocalTransform component was not registered.");
        var cameraRegistration = registration.TryGetComponentType<SceneComponentDefinitions.CameraComponent>(out var registeredCamera)
            ? registeredCamera ?? throw new InvalidOperationException("The Camera registration was null.")
            : throw new InvalidOperationException("The Camera component was not registered.");
        var spatialProfileId = new SpatialProfileId(Guid.NewGuid());
        var spatialSettings = new SpatialSettings(1, 12);
        SpatialProfiles.Register(spatialProfileId, spatialSettings);

        using var world = World.Create(new WorldOptions(registration) {
            InitialReplayLineageId = Guid.NewGuid(),
            SpatialProfileId = spatialProfileId,
            SpatialSettings = spatialSettings,
            WorldGuid = Guid.NewGuid(),
        });
        var entity = world.CreateEntity(new EntityDescription());
        world.Add<SceneComponentDefinitions.LocalTransformComponent>(entity);
        world.Add<SceneComponentDefinitions.RootPositionComponent>(entity);
        world.Add<SceneComponentDefinitions.CameraComponent>(entity);

        Assert.That(world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out var stored), Is.True);
        Assert.That(stored.Position, Is.EqualTo(Vector3.Zero));
        Assert.That(stored.Rotation, Is.EqualTo(Quaternion.Identity));
        Assert.That(stored.Scale, Is.EqualTo(Vector3.One));
        Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out var root), Is.True);
        Assert.That(root.Position, Is.EqualTo(default(SpatialPosition3)));
        Assert.That(world.TryGet<SceneComponentDefinitions.CameraComponent>(entity, out var camera), Is.True);
        Assert.That(camera.ProjectionKind, Is.EqualTo((byte)CameraProjectionKind.Perspective));
        Assert.That(camera.VerticalFieldOfView, Is.EqualTo(float.Pi / 3.0f));
        Assert.That(camera.OrthographicHeight, Is.EqualTo(1.0f));
        Assert.That(camera.Near, Is.EqualTo(0.01f));
        Assert.That(camera.Far.HasValue, Is.True);
        Assert.That(camera.Far.Value, Is.EqualTo(100.0f));
        Assert.That(camera.AspectRatioOverride.HasValue, Is.False);
        Assert.That(camera.Viewport, Is.EqualTo(new Vector4(0, 0, 1, 1)));
        Assert.That(camera.ClearFlags, Is.EqualTo((byte)(CameraClearFlags.Color | CameraClearFlags.Depth)));
        Assert.That(camera.ClearColor, Is.EqualTo(new Vector4(0, 0, 0, 1)));
        Assert.That(camera.LayerMask, Is.EqualTo(ulong.MaxValue));
        Assert.That(camera.Order, Is.Zero);
        var defaultCamera = camera;

        var assigned = SceneComponentDefinitions.CreateAssignedLocalTransform(
            new Vector3(1, 2, 3),
            new Quaternion(0, 0, 0, 2),
            new Vector3(-2, 3, 4));
        Assert.That(assigned.Rotation, Is.EqualTo(Quaternion.Identity));
        world.Set(entity, in assigned);
        Assert.That(world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out stored), Is.True);
        Assert.That(stored.Position, Is.EqualTo(assigned.Position));
        Assert.That(stored.Rotation, Is.EqualTo(assigned.Rotation));
        Assert.That(stored.Scale, Is.EqualTo(assigned.Scale));
        var assignedRoot = new SpatialPosition3(new ChunkIndex3(2, -3, 4), new Vector3(1, 2, 3));
        var rootValue = new SceneComponentDefinitions.RootPositionComponent(assignedRoot);
        world.Set(entity, in rootValue);
        Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out root), Is.True);
        Assert.That(root.Position, Is.EqualTo(assignedRoot));

        var infiniteCamera = new SceneComponentDefinitions.CameraComponent(
            (byte)CameraProjectionKind.Perspective,
            float.Pi / 3.0f,
            1,
            0.01f,
            (byte)CameraFarMode.Infinite,
            default,
            new SceneComponentDefinitions.OptionalSingle(16.0f / 9.0f),
            new Vector4(0, 0, 1, 1),
            (byte)(CameraClearFlags.Color | CameraClearFlags.Depth),
            new Vector4(0, 0, 0, 1),
            ulong.MaxValue,
            0);
        Assert.That(cameraRegistration.ValidateValue(in infiniteCamera, CancellationToken.None), Is.Empty);
        world.Set(entity, in infiniteCamera);
        Assert.That(world.TryGet<SceneComponentDefinitions.CameraComponent>(entity, out camera), Is.True);
        Assert.That(camera.Far.HasValue, Is.False);
        Assert.That(camera.AspectRatioOverride.HasValue, Is.True);
        Assert.That(camera.AspectRatioOverride.Value, Is.EqualTo(16.0f / 9.0f));
        world.Set(entity, in defaultCamera);

        var invalidCameras = new (SceneComponentDefinitions.CameraComponent Value, string Field)[]
        {
            (new(2, float.Pi / 3.0f, 1, 0.01f, 0, new(100), default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "ProjectionKind"),
            (new(0, float.NaN, 1, 0.01f, 0, new(100), default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "VerticalFieldOfView"),
            (new(0, float.Pi / 3.0f, 0, 0.01f, 0, new(100), default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "OrthographicHeight"),
            (new(0, float.Pi / 3.0f, 1, 0, 0, new(100), default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "Near"),
            (new(0, float.Pi / 3.0f, 1, 0.01f, 2, default, default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "FarMode"),
            (new(0, float.Pi / 3.0f, 1, 0.01f, 0, default, default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "Far"),
            (new(1, float.Pi / 3.0f, 1, 0.01f, 1, default, default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "FarMode"),
            (new(0, float.Pi / 3.0f, 1, 0.01f, 0, new(0.01f), default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "Far"),
            (new(0, float.Pi / 3.0f, 1, 0.01f, 0, new(100), new(0), new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "AspectRatioOverride"),
            (new(0, float.Pi / 3.0f, 1, 0.01f, 0, new(100), default, new Vector4(0.8f, 0, 0.3f, 1), 3, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "Viewport"),
            (new(0, float.Pi / 3.0f, 1, 0.01f, 0, new(100), default, new Vector4(0, 0, 1, 1), 4, new Vector4(0, 0, 0, 1), ulong.MaxValue, 0), "ClearFlags"),
            (new(0, float.Pi / 3.0f, 1, 0.01f, 0, new(100), default, new Vector4(0, 0, 1, 1), 3, new Vector4(0, 0, float.PositiveInfinity, 1), ulong.MaxValue, 0), "ClearColor"),
        };

        foreach (var (invalidCamera, field) in invalidCameras)
        {
            var diagnostics = cameraRegistration.ValidateValue(in invalidCamera, CancellationToken.None);

            Assert.That(diagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo("SceneCameraInvalid"));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostics[0].Operation, Is.EqualTo("Component.Validate"));
            Assert.That(diagnostics[0].Context, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Context[0], Is.EqualTo(new DiagnosticContextEntry("Field", field)));

            Assert.That(() => world.Set(entity, invalidCamera), Throws.TypeOf<ArgumentException>());
            Assert.That(world.TryGet<SceneComponentDefinitions.CameraComponent>(entity, out camera), Is.True);
            Assert.That(camera.Far.HasValue, Is.True);
            Assert.That(camera.Far.Value, Is.EqualTo(100.0f));
            Assert.That(camera.Viewport, Is.EqualTo(new Vector4(0, 0, 1, 1)));
        }

        var invalidValues = new (SceneComponentDefinitions.LocalTransformComponent Value, string Field)[]
        {
            (new(new Vector3(float.NaN, 0, 0), Quaternion.Identity, Vector3.One), "Position"),
            (new(Vector3.Zero, new Quaternion(0, 0, 0, 0), Vector3.One), "Rotation"),
            (new(Vector3.Zero, new Quaternion(float.MaxValue, 0, 0, 1), Vector3.One), "Rotation"),
            (new(Vector3.Zero, Quaternion.Identity, new Vector3(0.00009f, 1, 1)), "Scale"),
            (new(Vector3.Zero, Quaternion.Identity, new Vector3(1000.0001f, 1, 1)), "Scale"),
        };

        foreach (var (invalidValue, field) in invalidValues)
        {
            var diagnostics = transformRegistration.ValidateValue(in invalidValue, CancellationToken.None);

            Assert.That(diagnostics, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Code, Is.EqualTo("SceneLocalTransformInvalid"));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostics[0].Operation, Is.EqualTo("Component.Validate"));
            Assert.That(diagnostics[0].Context, Has.Length.EqualTo(1));
            Assert.That(diagnostics[0].Context[0], Is.EqualTo(new DiagnosticContextEntry("Field", field)));

            Assert.That(() => world.Set(entity, invalidValue), Throws.TypeOf<ArgumentException>());
            Assert.That(world.TryGet<SceneComponentDefinitions.LocalTransformComponent>(entity, out stored), Is.True);
            Assert.That(stored.Position, Is.EqualTo(assigned.Position));
            Assert.That(world.TryGet<SceneComponentDefinitions.RootPositionComponent>(entity, out root), Is.True);
            Assert.That(root.Position, Is.EqualTo(assignedRoot));
        }

        var receipt = world.Step();
        Assert.That(receipt.CompletedTick, Is.EqualTo(0));
    }

    [Test]
    public void ReadsAndRestoresLocalTransformFromItsCanonicalComponentRecord()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        var registration = builder.Build();
        var componentRegistration = registration.TryGetComponentType<SceneComponentDefinitions.LocalTransformComponent>(out var registered)
            ? registered ?? throw new InvalidOperationException("The LocalTransform component was not registered.")
            : throw new InvalidOperationException("The LocalTransform component was not registered.");
        var encodedRecord = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(
            new ContentSchemaIdentityAssignment("scene/local-transform").SchemaId,
            1,
            0,
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(1, 15, 2, 3, 4),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(2, 17, 0, 0, 0, 1),
            SceneHierarchyCodecTests.FixtureBuilder.CreateFloatGeometryField(3, 15, 2, 2, 2));
        var context = new RestoreContext();
        var result = componentRegistration.RestoreFromRecord(
            encodedRecord,
            context,
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(ComponentReadStatus.Read));
        Assert.That(result.TryGetInitialization(out var initialization), Is.True);
        var restoredInitialization = initialization
            ?? throw new InvalidOperationException("The successful component restore returned no initialization.");
        Assert.That(restoredInitialization.TryGetValue<SceneComponentDefinitions.LocalTransformComponent>(out var value), Is.True);
        Assert.That(value.Position, Is.EqualTo(new Vector3(2, 3, 4)));
        Assert.That(value.Rotation, Is.EqualTo(Quaternion.Identity));
        Assert.That(value.Scale, Is.EqualTo(new Vector3(2, 2, 2)));
    }

    [Test]
    public void RejectsMalformedComponentRecordAndPreservesDiagnostics()
    {
        var schemas = new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);
        var builder = new WorldRegistrationBuilder(schemas);
        _ = builder.RegisterSceneSupport(SceneSupport.CreateRegistration());
        var registration = builder.Build();
        var componentRegistration = registration.TryGetComponentType<SceneComponentDefinitions.LocalTransformComponent>(out var registered)
            ? registered ?? throw new InvalidOperationException("The LocalTransform component was not registered.")
            : throw new InvalidOperationException("The LocalTransform component was not registered.");
        var malformedRecord = SceneHierarchyCodecTests.FixtureBuilder.EncodeRecord(Guid.NewGuid(), 1, 0);

        var result = componentRegistration.RestoreFromRecord(
            malformedRecord,
            new RestoreContext(),
            CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(ComponentReadStatus.Rejected));
        Assert.That(result.TryGetInitialization(out _), Is.False);
        Assert.That(result.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(result.Diagnostics[0].Code, Is.EqualTo("ComponentRead.SchemaMismatch"));
    }

    private sealed class RestoreContext : IComponentRestoreContext
    {
        internal RestoreContext()
        {
            OwnerThreadId = Environment.CurrentManagedThreadId;
        }

        public bool IsActive => true;

        public int OwnerThreadId { get; }

        public TDependency ReadSameEntity<TDependency>()
            where TDependency : unmanaged
            => throw new InvalidOperationException("The LocalTransform restore does not read same-entity dependencies.");

        public Entity ResolveEntity(ComponentFieldDescriptor referenceField, Guid targetEntityId)
            => throw new InvalidOperationException("The LocalTransform restore does not resolve Entity references.");

        public Entity ResolveEntity(WorldSystemFieldDescriptor referenceField, Guid targetEntityId)
            => throw new InvalidOperationException("The LocalTransform restore does not resolve Entity references.");
    }
}
