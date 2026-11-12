// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Scenes;

internal sealed class PreparedAssetsLifetime : IDisposable
{
    private readonly Lock _gate = new();
    private bool _ownerReleased;
    private int _referenceCount = 1;
    private IDisposable? _value;

    internal PreparedAssetsLifetime(IDisposable value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    public void Dispose()
    {
        IDisposable? value = null;

        lock (_gate)
        {
            if (_ownerReleased)
            {
                return;
            }

            _ownerReleased = true;
            _referenceCount--;

            if (_referenceCount == 0)
            {
                value = _value;
                _value = null;
            }
        }

        value?.Dispose();
    }

    internal Lease Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_ownerReleased || (_value is null), this);

            if (_referenceCount == int.MaxValue)
            {
                throw new InvalidOperationException("The prepared asset lifetime has too many retained handles.");
            }

            _referenceCount++;

            try
            {
                return new Lease(this);
            }
            catch
            {
                _referenceCount--;
                throw;
            }
        }
    }

    internal IDisposable GetValue()
    {
        lock (_gate)
        {
            return _value
                ?? throw new ObjectDisposedException(nameof(PreparedAssetsLifetime));
        }
    }

    private void ReleaseLease()
    {
        IDisposable? value = null;

        lock (_gate)
        {
            _referenceCount--;

            if (_referenceCount < 0)
            {
                throw new InvalidOperationException("The prepared asset reference count became negative.");
            }

            if (_referenceCount == 0)
            {
                value = _value;
                _value = null;
            }
        }

        value?.Dispose();
    }

    internal sealed class Lease : IDisposable
    {
        private PreparedAssetsLifetime? _lifetime;

        internal Lease(PreparedAssetsLifetime lifetime)
        {
            ArgumentNullException.ThrowIfNull(lifetime);
            _lifetime = lifetime;
        }

        internal IDisposable Value
        {
            get
            {
                var lifetime = Volatile.Read(ref _lifetime);
                ObjectDisposedException.ThrowIf(lifetime is null, this);
                return lifetime.GetValue();
            }
        }

        public void Dispose()
            => Interlocked.Exchange(ref _lifetime, null)?.ReleaseLease();
    }
}
