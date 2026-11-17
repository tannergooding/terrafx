// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Entities;

internal interface IWorldResourceOptionalArrayReader<TOwner, TElement>
{
    int GetCount(in TOwner value);

    TElement GetElement(in TOwner value, int index);

    bool IsPresent(in TOwner value);
}
