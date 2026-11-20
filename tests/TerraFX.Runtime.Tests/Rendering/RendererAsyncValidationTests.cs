// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;
using NUnit.Framework;
using TerraFX.Rendering;

namespace TerraFX.Runtime.Tests.Rendering;

[TestFixture]
internal sealed class RendererAsyncValidationTests
{
    [Test]
    public void SubmitAsyncChecksNullSnapshotSynchronouslyBeforePreCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var renderer = Renderer.CreateHeadless();

        var exception = Assert.Throws<ArgumentNullException>(
            () => renderer.SubmitAsync(null!, null!, cancellationSource.Token));

        Assert.That(exception?.ParamName, Is.EqualTo("snapshot"));
    }
}
