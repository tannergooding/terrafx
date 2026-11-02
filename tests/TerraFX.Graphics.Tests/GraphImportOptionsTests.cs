// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using NUnit.Framework;

namespace TerraFX.Graphics.Tests;

[TestFixture]
internal sealed class GraphImportOptionsTests
{
    [Test]
    public void ImportOptionsRejectInvalidStatesAndUndefinedValidContents()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphImportOptions((GraphResourceState)(-1), false, GraphImportCompletionPolicy.WaitForLastUse));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => new GraphImportOptions(GraphResourceState.Undefined, false, (GraphImportCompletionPolicy)(-1)));
        _ = Assert.Throws<ArgumentException>(() => new GraphImportOptions(GraphResourceState.Undefined, true, GraphImportCompletionPolicy.WaitForLastUse));
    }

    [Test]
    public void ImportOptionsRetainTrackedStateContentsAndCompletionPolicy()
    {
        var options = new GraphImportOptions(
            GraphResourceState.General,
            true,
            GraphImportCompletionPolicy.RequireIdle);

        Assert.That((int)GraphResourceState.General, Is.EqualTo(13));
        Assert.That(options.InitialState, Is.EqualTo(GraphResourceState.General));
        Assert.That(options.ContentsValid, Is.True);
        Assert.That(options.CompletionPolicy, Is.EqualTo(GraphImportCompletionPolicy.RequireIdle));
    }
}
