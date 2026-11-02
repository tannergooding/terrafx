// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using TerraFX;
using TerraFX.Input;

namespace TerraFX.Platform;

internal sealed class PlatformInputSampleQueue
{
    internal const int DefaultCapacity = 4096;

    private int _count;
    private long _discardedSampleCount;
    private int _head;
    private readonly int _ownerThreadId;
    private readonly PlatformInputSample[] _samples;

    internal PlatformInputSampleQueue(int ownerThreadId, int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ownerThreadId, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _ownerThreadId = ownerThreadId;
        _samples = new PlatformInputSample[capacity];
    }

    internal PlatformInputSampleBatch Drain()
    {
        EnsureOwnerThread();
        var samples = ImmutableArray.CreateBuilder<PlatformInputSample>(_count);

        while (_count != 0)
        {
            samples.Add(_samples[_head]);
            _samples[_head] = default;
            _head = (_head + 1) % _samples.Length;
            _count--;
        }

        _head = 0;
        var discardedSampleCount = _discardedSampleCount;
        _discardedSampleCount = 0;
        return new PlatformInputSampleBatch(samples.MoveToImmutable(), discardedSampleCount);
    }

    internal bool TryEnqueue(in PlatformInputSample sample)
    {
        EnsureOwnerThread();

        if (!Enum.IsDefined(sample.Kind) || (sample.Kind == PhysicalInputEventKind.None))
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }

        if (!Enum.IsDefined(sample.DeviceKind)
            || (sample.Window is { IsValid: false })
            || ((sample.NativeDeviceIdentity == 0)
                && (sample.Kind is PhysicalInputEventKind.DeviceAdded or PhysicalInputEventKind.DeviceRemoved)))
        {
            throw new ArgumentException("The platform input sample has an invalid source.", nameof(sample));
        }

        if (_count == _samples.Length)
        {
            _discardedSampleCount = checked(_discardedSampleCount + 1);
            return false;
        }

        var tail = (_head + _count) % _samples.Length;
        _samples[tail] = sample;
        _count++;
        return true;
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("The platform input sample queue is restricted to its owner thread.");
        }
    }
}
