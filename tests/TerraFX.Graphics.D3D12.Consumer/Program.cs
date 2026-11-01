// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Versioning;
using TerraFX.Graphics;
using TerraFX.Graphics.D3D12;

namespace TerraFX.Graphics.D3D12.Consumer;

internal static class Program
{
    [SupportedOSPlatform("windows10.0.22000.0")]
    [SuppressMessage("Globalization", "CA1303", Justification = "The output is a machine-checked consumer witness marker.")]
    private static void Main()
    {
        var registration = D3D12GraphicsBackend.CreateRegistration();

        if (registration.Backend != GraphicsBackend.D3D12)
        {
            throw new InvalidOperationException("The selected backend registration did not preserve D3D12 identity.");
        }

        if (typeof(GraphicsDevice).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length != 0)
        {
            throw new InvalidOperationException("GraphicsDevice must not expose public owner construction.");
        }

        if (typeof(GpuBuffer).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length != 0)
        {
            throw new InvalidOperationException("GpuBuffer must not expose public resource construction.");
        }

        if (typeof(GpuTexture).GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length != 0)
        {
            throw new InvalidOperationException("GpuTexture must not expose public resource construction.");
        }

        Console.WriteLine("D3D12 public registration consumer passed with unsafe disabled, no public owner/resource construction, and no native device activation.");
    }
}
