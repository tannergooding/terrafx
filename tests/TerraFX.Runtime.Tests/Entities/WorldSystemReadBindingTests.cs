// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;
using TerraFX;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests.Entities;

[TestFixture]
internal sealed class WorldSystemReadBindingTests
{
    [Test]
    public void CompletePreservesAuthorValueAndExactConsumption()
    {
        var systemId = Guid.NewGuid();
        var schema = new ContentSchemaReference(
            Guid.NewGuid(),
            1,
            0,
            ContentSchemaScope.Content);
        var binding = new WorldSystemReadBinding<ProbeAuthor>(systemId, schema, 3);
        var author = new ProbeAuthor(42);

        var result = binding.Complete(author, 3);

        Assert.That(result.Status, Is.EqualTo(WorldSystemReadStatus.Read));
        Assert.That(result.ConsumedBytes, Is.EqualTo((nint)3));
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.TryGetAuthorValue(out var value), Is.True);
        Assert.That(value, Is.SameAs(author));
    }

    [Test]
    public void CompleteRejectsAResultIssuedByAnotherBinding()
    {
        var schema = new ContentSchemaReference(
            Guid.NewGuid(),
            1,
            0,
            ContentSchemaScope.Content);
        var issuedBinding = new WorldSystemReadBinding<ProbeAuthor>(Guid.NewGuid(), schema, 3);
        var receivingBinding = new WorldSystemReadBinding<ProbeAuthor>(Guid.NewGuid(), schema, 3);
        var result = issuedBinding.Complete(new ProbeAuthor(5), 3);

        var failure = Assert.Throws<InvalidOperationException>(() => receivingBinding.ValidateComplete(result));

        Assert.That(receivingBinding.Failure, Is.SameAs(failure));
    }

    [Test]
    public void InvalidCompletionRemainsStickyAfterCallbackCatchesIt()
    {
        var schema = new ContentSchemaReference(
            Guid.NewGuid(),
            1,
            0,
            ContentSchemaScope.Content);
        var binding = new WorldSystemReadBinding<ProbeAuthor>(Guid.NewGuid(), schema, 3);
        var author = new ProbeAuthor(1);

        var firstFailure = Assert.Throws<InvalidOperationException>(() => binding.Complete(author, 2));
        var repeatedFailure = Assert.Throws<InvalidOperationException>(() => binding.Complete(author, 3));

        Assert.That(binding.Failure, Is.SameAs(firstFailure));
        Assert.That(repeatedFailure, Is.SameAs(firstFailure));
    }

    [Test]
    public void RejectReturnsDiagnosticWithoutAnAuthorValue()
    {
        var systemId = Guid.NewGuid();
        var schema = new ContentSchemaReference(
            Guid.NewGuid(),
            1,
            0,
            ContentSchemaScope.Content);
        var binding = new WorldSystemReadBinding<ProbeAuthor>(systemId, schema, 3);
        var path = new ComponentFieldPath([ComponentPropertySegment.FieldTag(4)]);

        var result = binding.Reject(
            WorldSystemReadFailureKind.InvalidField,
            2,
            path,
            "The field is invalid.");

        Assert.That(result.Status, Is.EqualTo(WorldSystemReadStatus.Rejected));
        Assert.That(result.ConsumedBytes, Is.EqualTo((nint)0));
        Assert.That(result.Diagnostics, Has.Length.EqualTo(1));
        Assert.That(result.Diagnostics[0].SystemId, Is.EqualTo(systemId));
        Assert.That(result.Diagnostics[0].Message, Is.EqualTo("The field is invalid."));
        Assert.That(result.TryGetAuthorValue(out _), Is.False);
    }

    private sealed class ProbeAuthor
    {
        internal ProbeAuthor(int value)
        {
            Value = value;
        }

        internal int Value { get; }
    }
}
