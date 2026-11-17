// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal readonly record struct GraphicsGraphSubmissionPass(
    RenderPassId PassId,
    CommandRecorderPassKind Kind,
    RenderArea? RenderArea,
    RecordedPassCommandList? CommandList,
    ImmutableArray<GraphicsGraphSubmissionAttachment> Attachments)
{
    internal RecordedPassCommandList.CommandRead ReadCommands(ref int readState)
    {
        if (CommandList is null)
        {
            return RecordedPassCommandList.CommandRead.CreateEmpty(ref readState);
        }

        if ((CommandList.PassId != PassId) || (CommandList.PassKind != Kind))
        {
            throw new InvalidOperationException("The recorded command owner does not match its submission pass.");
        }

        return CommandList.ReadCommands(ref readState);
    }
}
