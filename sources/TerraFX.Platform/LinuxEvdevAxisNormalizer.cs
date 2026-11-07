// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.IO;

namespace TerraFX.Platform;

internal static class LinuxEvdevAxisNormalizer
{
    internal static bool HasSignificantChange(int previousValue, int currentValue, LinuxEvdevAxisInfo axisInfo)
    {
        ValidateAxisInfo(axisInfo);
        ValidateRawValue(previousValue, axisInfo);
        ValidateRawValue(currentValue, axisInfo);
        return Math.Abs((long)currentValue - previousValue) > axisInfo.Fuzz;
    }

    internal static float NormalizeCenteredAxis(int value, LinuxEvdevAxisInfo axisInfo)
    {
        ValidateAxisInfo(axisInfo);
        ValidateRawValue(value, axisInfo);

        var midpoint = ((double)axisInfo.Minimum + axisInfo.Maximum) / 2.0;
        var negativeRange = midpoint - axisInfo.Minimum;
        var positiveRange = axisInfo.Maximum - midpoint;

        if ((axisInfo.Flat >= negativeRange) || (axisInfo.Flat >= positiveRange))
        {
            throw new InvalidDataException("An evdev centered axis flat zone consumes its usable range.");
        }

        var delta = value - midpoint;

        if (Math.Abs(delta) <= axisInfo.Flat)
        {
            return 0.0f;
        }

        var normalizedRange = (delta < 0.0)
            ? negativeRange - axisInfo.Flat
            : positiveRange - axisInfo.Flat;
        var normalizedDelta = (delta < 0.0)
            ? delta + axisInfo.Flat
            : delta - axisInfo.Flat;
        return checked((float)(normalizedDelta / normalizedRange));
    }

    internal static float NormalizeTriggerAxis(int value, LinuxEvdevAxisInfo axisInfo)
    {
        ValidateAxisInfo(axisInfo);
        ValidateRawValue(value, axisInfo);
        var range = (double)axisInfo.Maximum - axisInfo.Minimum;

        if (axisInfo.Flat >= range)
        {
            throw new InvalidDataException("An evdev trigger flat zone consumes its usable range.");
        }

        var distance = (double)value - axisInfo.Minimum;
        return (distance <= axisInfo.Flat)
            ? 0.0f
            : checked((float)((distance - axisInfo.Flat) / (range - axisInfo.Flat)));
    }

    private static void ValidateAxisInfo(LinuxEvdevAxisInfo axisInfo)
    {
        if ((axisInfo.Maximum <= axisInfo.Minimum)
            || (axisInfo.Fuzz < 0)
            || (axisInfo.Flat < 0))
        {
            throw new InvalidDataException("An evdev absolute-axis range or noise descriptor is invalid.");
        }
    }

    private static void ValidateRawValue(int value, LinuxEvdevAxisInfo axisInfo)
    {
        if ((value < axisInfo.Minimum) || (value > axisInfo.Maximum))
        {
            throw new InvalidDataException("An evdev absolute-axis sample is outside its reported range.");
        }
    }
}
