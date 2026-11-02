// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal interface IComponentCaptureRegistration : IComponentRegistration
{
    nint CountStateRecord(
        object column,
        int row,
        IComponentCaptureContext context,
        CancellationToken cancellationToken);

    bool IsEntityReferenceField(ComponentFieldDescriptor referenceField);

    nint WriteStateRecord(
        object column,
        int row,
        scoped Span<byte> destination,
        IComponentCaptureContext context,
        CancellationToken cancellationToken);
}
