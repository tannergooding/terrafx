// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Threading.Tasks;
using NUnit.Framework;

namespace TerraFX.UnitTests;

[TestFixture]
internal sealed class ReentrantGateTests
{
    [Test]
    public void ConcurrentScopesExcludeOtherThreads()
    {
        var gate = new ReentrantGate();
        var value = 0;

        _ = Parallel.For(0, 8, _ => {
            for (var iteration = 0; iteration < 1024; iteration++)
            {
                using (ReentrantGate.EnterScope(ref gate))
                {
                    value++;
                }
            }
        });

        Assert.That(value, Is.EqualTo(8192));
    }

    [Test]
    public void GateUsesValueStorage()
    {
        Assert.That(typeof(ReentrantGate).IsValueType, Is.True);
    }

    [Test]
    public void NestedScopesRemainReentrant()
    {
        var gate = new ReentrantGate();

        using (ReentrantGate.EnterScope(ref gate))
        {
            using (ReentrantGate.EnterScope(ref gate))
            {
            }
        }
    }
}
