// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class ExecutionSessionTests
{
    [Test]
    public void ConcurrentAccessPublishesOneNonEmptySessionId()
    {
        var ids = new Guid[256];

        _ = Parallel.For(0, ids.Length, index => ids[index] = ExecutionSession.ExecutionSessionId);

        var sessionId = ids[0];

        Assert.That(sessionId, Is.Not.EqualTo(Guid.Empty));

        foreach (var id in ids)
        {
            Assert.That(id, Is.EqualTo(sessionId));
        }

        Assert.That(ExecutionSession.ExecutionSessionId, Is.EqualTo(sessionId));
    }
}
