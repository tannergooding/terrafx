// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Content.Tools;

internal readonly record struct ContentPackageAsset(uint AssetId, ReadOnlyMemory<byte> Record);
