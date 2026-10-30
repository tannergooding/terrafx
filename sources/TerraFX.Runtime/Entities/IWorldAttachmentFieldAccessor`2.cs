// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldAttachmentFieldAccessor<T, TAuthor>
    where T : class
    where TAuthor : notnull
{
    void ValidateAuthor(in TAuthor author);

    void ValidateRuntime(T value, IWorldAttachmentValidationContext context);
}
