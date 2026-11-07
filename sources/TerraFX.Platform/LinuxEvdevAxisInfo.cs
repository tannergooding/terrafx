// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Platform;

internal readonly record struct LinuxEvdevAxisInfo(
    int Minimum,
    int Maximum,
    int Fuzz,
    int Flat);
