// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using TerraFX.Entities;

namespace TerraFX.Runtime.Tests;

[TestFixture]
internal sealed class CoreOwnerIdFriendshipTests
{
    [Test]
    public void CoreFriendsRuntimeWithTheExpectedPublicKey()
    {
        var coreAssembly = Assembly.Load(new AssemblyName("TerraFX.Core"));
        var runtimeName = typeof(Entity).Assembly.GetName();
        InternalsVisibleToAttribute? runtimeFriend = null;

        foreach (var friend in coreAssembly.GetCustomAttributes<InternalsVisibleToAttribute>())
        {
            var friendName = new AssemblyName(friend.AssemblyName);

            if (friendName.Name == runtimeName.Name)
            {
                Assert.That(runtimeFriend, Is.Null);
                runtimeFriend = friend;
            }
        }

        Assert.That(runtimeFriend, Is.Not.Null);
        var declaredRuntimeName = new AssemblyName(runtimeFriend?.AssemblyName ?? throw new AssertionException("TerraFX.Core did not declare Runtime as a friend assembly."));
        Assert.That(declaredRuntimeName.GetPublicKey(), Is.EqualTo(runtimeName.GetPublicKey()));
    }
}
