// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System.Collections.Immutable;

namespace TerraFX.Graphics;

internal readonly record struct GraphicsGraphSubmissionPass(
    RenderPassId PassId,
    CommandRecorderPassKind Kind,
    ImmutableArray<RecordedGraphicsCommand> Commands,
    ImmutableArray<GraphicsGraphSubmissionAttachment> Attachments);
