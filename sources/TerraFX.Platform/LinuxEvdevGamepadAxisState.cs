// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TerraFX.Input;
using TerraFX.Numerics;

namespace TerraFX.Platform;

internal sealed class LinuxEvdevGamepadAxisState
{
    private const ushort AbsRx = 0x0003;
    private const ushort AbsRy = 0x0004;
    private const ushort AbsRz = 0x0005;
    private const ushort AbsX = 0x0000;
    private const ushort AbsY = 0x0001;
    private const ushort AbsZ = 0x0002;

    private readonly Dictionary<ushort, AxisState> _axes = [];
    private float _leftTrigger;
    private float _leftX;
    private float _leftY;
    private float _rightTrigger;
    private float _rightX;
    private float _rightY;

    internal LinuxEvdevGamepadAxisState(
        ReadOnlySpan<(ushort Code, LinuxEvdevAxisInfo Info, int InitialValue)> descriptors)
    {
        foreach (var (code, info, initialValue) in descriptors)
        {
            var profile = GetProfile(code);
            var normalizedValue = Normalize(code, initialValue, info);
            var axis = new AxisState(info, initialValue, profile);

            if (!_axes.TryAdd(code, axis))
            {
                throw new ArgumentException("The evdev gamepad axis profile contains a duplicate code.", nameof(descriptors));
            }

            SetNormalizedValue(profile, normalizedValue);
        }
    }

    private static PlatformInputStateValue CreateAxisState(GamepadAxis axis, float value)
        => new(
            PlatformInputStateValueKind.GamepadAxis,
            Axis: axis,
            AxisValue: value);

    private static PlatformInputStateValue CreateStickState(GamepadStick stick, Vector2 value)
        => new(
            PlatformInputStateValueKind.GamepadStick,
            Stick: stick,
            StickValue: value);

    private static PlatformInputStateValue CreateTriggerState(GamepadTrigger trigger, float value)
        => new(
            PlatformInputStateValueKind.GamepadTrigger,
            Trigger: trigger,
            TriggerValue: value);

    private static (GamepadAxis? Axis, GamepadTrigger? Trigger, bool InvertY) GetProfile(ushort code)
        => code switch {
            AbsX => (GamepadAxis.LeftX, null, false),
            AbsY => (GamepadAxis.LeftY, null, true),
            AbsZ => (null, GamepadTrigger.Left, false),
            AbsRx => (GamepadAxis.RightX, null, false),
            AbsRy => (GamepadAxis.RightY, null, true),
            AbsRz => (null, GamepadTrigger.Right, false),
            _ => throw new ArgumentOutOfRangeException(nameof(code), "The evdev axis code is outside the standard gamepad profile."),
        };

    private static GamepadStick GetStickForAxis(GamepadAxis axis)
        => axis switch {
            GamepadAxis.LeftX or GamepadAxis.LeftY => GamepadStick.Left,
            GamepadAxis.RightX or GamepadAxis.RightY => GamepadStick.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

    private static float Normalize(
        ushort code,
        int rawValue,
        LinuxEvdevAxisInfo info)
    {
        var (_, trigger, invertY) = GetProfile(code);
        var value = trigger is null
            ? LinuxEvdevAxisNormalizer.NormalizeCenteredAxis(rawValue, info)
            : LinuxEvdevAxisNormalizer.NormalizeTriggerAxis(rawValue, info);
        return invertY ? -value : value;
    }

    private static bool VectorEquals(Vector2 left, Vector2 right)
        => (left.X == right.X) && (left.Y == right.Y);

    internal ImmutableArray<PlatformInputStateValue> GetCurrentState()
        => [
            CreateAxisState(GamepadAxis.LeftX, _leftX),
            CreateAxisState(GamepadAxis.LeftY, _leftY),
            CreateAxisState(GamepadAxis.RightX, _rightX),
            CreateAxisState(GamepadAxis.RightY, _rightY),
            CreateStickState(GamepadStick.Left, GetStick(GamepadStick.Left)),
            CreateStickState(GamepadStick.Right, GetStick(GamepadStick.Right)),
            CreateTriggerState(GamepadTrigger.Left, _leftTrigger),
            CreateTriggerState(GamepadTrigger.Right, _rightTrigger),
        ];

    internal bool TryUpdate(
        ushort code,
        int rawValue,
        out ImmutableArray<PlatformInputStateValue> changes)
    {
        changes = [];

        if (!_axes.TryGetValue(code, out var axis))
        {
            return false;
        }

        if (!LinuxEvdevAxisNormalizer.HasSignificantChange(axis.RawValue, rawValue, axis.Info))
        {
            return false;
        }

        var normalizedValue = Normalize(code, rawValue, axis.Info);
        var previousAxisValue = axis.Profile.Axis is GamepadAxis gamepadAxis
            ? GetAxisValue(gamepadAxis)
            : 0.0f;
        var previousTriggerValue = axis.Profile.Trigger is GamepadTrigger gamepadTrigger
            ? GetTriggerValue(gamepadTrigger)
            : 0.0f;
        var previousStick = axis.Profile.Axis is GamepadAxis selectedAxis
            ? GetStick(GetStickForAxis(selectedAxis))
            : default;

        axis.RawValue = rawValue;
        SetNormalizedValue(axis.Profile, normalizedValue);

        var builder = ImmutableArray.CreateBuilder<PlatformInputStateValue>(2);

        if (axis.Profile.Axis is GamepadAxis updatedAxis)
        {
            if (GetAxisValue(updatedAxis) != previousAxisValue)
            {
                builder.Add(CreateAxisState(updatedAxis, GetAxisValue(updatedAxis)));
            }

            var stick = GetStickForAxis(updatedAxis);
            var currentStick = GetStick(stick);

            if (!VectorEquals(previousStick, currentStick))
            {
                builder.Add(CreateStickState(stick, currentStick));
            }
        }
        else if (axis.Profile.Trigger is GamepadTrigger updatedTrigger)
        {
            var currentTrigger = GetTriggerValue(updatedTrigger);

            if (currentTrigger != previousTriggerValue)
            {
                builder.Add(CreateTriggerState(updatedTrigger, currentTrigger));
            }
        }

        changes = builder.ToImmutable();
        return !changes.IsEmpty;
    }

    private float GetAxisValue(GamepadAxis axis)
        => axis switch {
            GamepadAxis.LeftX => _leftX,
            GamepadAxis.LeftY => _leftY,
            GamepadAxis.RightX => _rightX,
            GamepadAxis.RightY => _rightY,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

    private Vector2 GetStick(GamepadStick stick)
    {
        var (x, y) = stick switch {
            GamepadStick.Left => (_leftX, _leftY),
            GamepadStick.Right => (_rightX, _rightY),
            _ => throw new ArgumentOutOfRangeException(nameof(stick)),
        };
        var lengthSquared = (x * x) + (y * y);

        if (lengthSquared > 1.0f)
        {
            var inverseLength = 1.0f / MathF.Sqrt(lengthSquared);
            x *= inverseLength;
            y *= inverseLength;
        }

        return new Vector2(x, y);
    }

    private float GetTriggerValue(GamepadTrigger trigger)
        => trigger switch {
            GamepadTrigger.Left => _leftTrigger,
            GamepadTrigger.Right => _rightTrigger,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

    private void SetNormalizedValue(
        (GamepadAxis? Axis, GamepadTrigger? Trigger, bool InvertY) profile,
        float value)
    {
        if (profile.Axis is GamepadAxis axis)
        {
            switch (axis)
            {
                case GamepadAxis.LeftX:
                {
                    _leftX = value;
                    break;
                }

                case GamepadAxis.LeftY:
                {
                    _leftY = value;
                    break;
                }

                case GamepadAxis.RightX:
                {
                    _rightX = value;
                    break;
                }

                case GamepadAxis.RightY:
                {
                    _rightY = value;
                    break;
                }

                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(profile));
                }
            }
        }
        else if (profile.Trigger is GamepadTrigger trigger)
        {
            switch (trigger)
            {
                case GamepadTrigger.Left:
                {
                    _leftTrigger = value;
                    break;
                }

                case GamepadTrigger.Right:
                {
                    _rightTrigger = value;
                    break;
                }

                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(profile));
                }
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }
    }

    private sealed class AxisState
    {
        internal AxisState(
            LinuxEvdevAxisInfo info,
            int rawValue,
            (GamepadAxis? Axis, GamepadTrigger? Trigger, bool InvertY) profile)
        {
            Info = info;
            RawValue = rawValue;
            Profile = profile;
        }

        internal LinuxEvdevAxisInfo Info { get; }

        internal (GamepadAxis? Axis, GamepadTrigger? Trigger, bool InvertY) Profile { get; }

        internal int RawValue { get; set; }
    }
}
