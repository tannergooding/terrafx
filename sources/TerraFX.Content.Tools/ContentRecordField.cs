// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Content.Tools;

internal readonly record struct ContentRecordField(uint Tag, ushort Kind, ulong Count, byte[] Payload);
