// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX;

internal readonly record struct FoundationNameAssignment(Guid Id, Guid NamespaceId, string Name, string Recipe);
