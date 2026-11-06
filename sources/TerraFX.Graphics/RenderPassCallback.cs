// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

namespace TerraFX.Graphics;

/// <summary>Records one pass synchronously on the graphics owner thread.</summary>
/// <param name="recorder">The scoped pass recorder.</param>
public delegate void RenderPassCallback(scoped ref CommandRecorder recorder);
