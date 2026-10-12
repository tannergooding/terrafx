// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Core.Tests;

[TestFixture]
internal sealed class ContentSchemaReferenceTests
{
    [Test]
    public void ConstructorRejectsInvalidIdentityVersionAndScope()
    {
        var schemaId = new Guid("2ed8b750-e1a0-4b46-9000-000000000001");

        _ = Assert.Throws<ArgumentException>(() => _ = new ContentSchemaReference(Guid.Empty, 1, 0, ContentSchemaScope.Content));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ContentSchemaReference(schemaId, 0, 0, ContentSchemaScope.Content));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ContentSchemaReference(schemaId, 1, -1, ContentSchemaScope.Content));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ContentSchemaReference(schemaId, 1, 0, (ContentSchemaScope)2));
    }

    [Test]
    public void EqualityUsesTheCompleteTupleAndDefaultIsInvalid()
    {
        var schemaId = new Guid("2ed8b750-e1a0-4b46-9000-000000000001");
        var reference = new ContentSchemaReference(schemaId, 1, 2, ContentSchemaScope.Content);
        var equalReference = new ContentSchemaReference(schemaId, 1, 2, ContentSchemaScope.Content);
        var otherSchema = new ContentSchemaReference(Guid.NewGuid(), 1, 2, ContentSchemaScope.Content);
        var otherMajor = new ContentSchemaReference(schemaId, 2, 2, ContentSchemaScope.Content);
        var otherScope = new ContentSchemaReference(schemaId, 1, 2, ContentSchemaScope.Foundation);
        var otherMinor = new ContentSchemaReference(schemaId, 1, 3, ContentSchemaScope.Content);

        Assert.That(default(ContentSchemaReference).SchemaId, Is.EqualTo(Guid.Empty));
        Assert.That(reference == equalReference, Is.True);
        Assert.That(reference != otherSchema, Is.True);
        Assert.That(reference != otherMajor, Is.True);
        Assert.That(reference != otherScope, Is.True);
        Assert.That(reference != otherMinor, Is.True);
        Assert.That(reference.Equals((object)equalReference), Is.True);
        Assert.That(reference.GetHashCode(), Is.EqualTo(equalReference.GetHashCode()));
    }
}
