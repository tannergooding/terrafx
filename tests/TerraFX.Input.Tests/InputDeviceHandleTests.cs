// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using NUnit.Framework;

namespace TerraFX.Input.Tests;

[TestFixture]
internal sealed class InputDeviceHandleTests
{
    [Test]
    public void DefaultIsInvalidAndIssuedIdentityIncludesGeneration()
    {
        var first = new InputDeviceHandle(1, 0, 1);
        var same = new InputDeviceHandle(1, 0, 1);

        Assert.That(default(InputDeviceHandle).IsValid, Is.False);
        Assert.That(first.IsValid, Is.True);
        Assert.That(first, Is.EqualTo(same));
        Assert.That(first.GetHashCode(), Is.EqualTo(same.GetHashCode()));
        Assert.That(first, Is.Not.EqualTo(new InputDeviceHandle(2, 0, 1)));
        Assert.That(first, Is.Not.EqualTo(new InputDeviceHandle(1, 1, 1)));
        Assert.That(first, Is.Not.EqualTo(new InputDeviceHandle(1, 0, 2)));
    }

    [Test]
    public void IssuedIdentityRequiresOwnerAndGeneration()
    {
        Assert.That(() => new InputDeviceHandle(0, 0, 1), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        Assert.That(() => new InputDeviceHandle(1, 0, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
    }
}
