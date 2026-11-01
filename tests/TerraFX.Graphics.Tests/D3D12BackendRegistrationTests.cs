// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Runtime.Versioning;
using NUnit.Framework;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class D3D12BackendRegistrationTests
{
    [Test]
    [SupportedOSPlatform("windows10.0.22000.0")]
    public void CreateRegistrationReturnsTheExplicitD3D12Backend()
    {
        var registration = D3D12GraphicsBackend.CreateRegistration();

        Assert.That(registration.Backend, Is.EqualTo(GraphicsBackend.D3D12));
    }
}
