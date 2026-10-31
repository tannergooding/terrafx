// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Assets;
using TerraFX.Entities;
using TerraFX.Scenes;

namespace TerraFX.Runtime.Tests.Scenes;

[TestFixture]
internal sealed class SceneSupportRegistrationTests
{
    private static CompiledContentSchemaRegistry CreateSchemaRegistry()
        => new CompiledContentSchemaRegistry([
            .. CoreContentSchemaCatalogue.PrimitiveSchemas,
            .. CoreContentSchemaCatalogue.FoundationSchemas,
            .. NativeAssetSchemaCatalogue.Schemas,
            .. SceneSchemaCatalogue.Schemas,
        ]);

    private static ContentSchemaReference GetSceneComponentSchema(string name)
    {
        foreach (var schema in SceneSchemaCatalogue.Schemas)
        {
            if (StringComparer.Ordinal.Equals(schema.IdentityAssignment.Name, name))
            {
                return schema.Schema;
            }
        }

        throw new InvalidOperationException($"The Scene schema '{name}' is not registered.");
    }

    [Test]
    public void RegistersBuiltInComponentsBeforeWorldConstruction()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        var support = SceneSupport.CreateRegistration();

        Assert.That(builder.RegisterSceneSupport(support), Is.SameAs(builder));
        var registration = builder.Build();

        Assert.That(registration.SceneSupport, Is.SameAs(support));

        foreach (var schemaName in new[] { "scene/local-transform", "scene/root-position", "scene/camera" })
        {
            var schema = GetSceneComponentSchema(schemaName);
            Assert.That(registration.GetComponentBinding(schema).Schema, Is.EqualTo(schema));
        }
    }

    [Test]
    public void RejectsConflictingComponentsAtomically()
    {
        var schemas = CreateSchemaRegistry();
        var builder = new WorldRegistrationBuilder(schemas);
        _ = SceneComponentDefinitions.RegisterCamera(builder, schemas);

        Assert.That(
            () => builder.RegisterSceneSupport(SceneSupport.CreateRegistration()),
            Throws.ArgumentException);

        var registration = builder.Build();
        Assert.That(registration.TryGetComponentType<SceneComponentDefinitions.LocalTransformComponent>(out _), Is.False);
        Assert.That(registration.TryGetComponentType<SceneComponentDefinitions.RootPositionComponent>(out _), Is.False);
        Assert.That(registration.TryGetComponentType<SceneComponentDefinitions.CameraComponent>(out _), Is.True);
    }

    [Test]
    public void RejectsDuplicateSceneSupport()
    {
        var builder = new WorldRegistrationBuilder(CreateSchemaRegistry());
        var support = SceneSupport.CreateRegistration();

        _ = builder.RegisterSceneSupport(support);

        Assert.That(
            () => builder.RegisterSceneSupport(SceneSupport.CreateRegistration()),
            Throws.ArgumentException);
        Assert.That(builder.Build().SceneSupport, Is.SameAs(support));
    }
}
