// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldAttachmentRecordBindingNode
{
    int Depth { get; }

    ContentSchemaRole Role { get; }

    ContentSchemaReference Schema { get; }

    CompiledContentSchemaRegistry Schemas { get; }

    void ValidateNestedRoles();
}
