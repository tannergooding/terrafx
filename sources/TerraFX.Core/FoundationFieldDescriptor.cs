// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX;

internal readonly record struct FoundationFieldDescriptor(uint Tag, ushort Kind, ulong Minimum, ulong Maximum, bool Required = true);
