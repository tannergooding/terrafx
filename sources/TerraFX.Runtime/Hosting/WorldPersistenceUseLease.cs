// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Hosting;

internal sealed class WorldPersistenceUseLease : IDisposable
{
    private readonly IDisposable _first;
    private int _isDisposed;
    private readonly IDisposable _second;
    private readonly IDisposable _third;

    internal WorldPersistenceUseLease(IDisposable first, IDisposable second, IDisposable third)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        _first = first;
        _second = second;
        _third = third;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            _third.Dispose();
        }
        finally
        {
            try
            {
                _second.Dispose();
            }
            finally
            {
                _first.Dispose();
            }
        }
    }
}
