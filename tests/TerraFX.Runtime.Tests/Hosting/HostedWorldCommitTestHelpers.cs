// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using TerraFX.Entities;
using TerraFX.Hosting;

namespace TerraFX.Runtime.Tests.Hosting;

internal static class HostedWorldCommitTestHelpers
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    internal static Task InvokeAtCommittedFrameAsync(
        EngineHost host,
        World world,
        Action<World> action)
        => InvokeWhenReadyAsync(host, world, TryAcquireFrameReadLease, action);

    internal static Task InvokeAtPersistenceCaptureAsync(
        EngineHost host,
        World world,
        Action<World> action)
        => InvokeWhenReadyAsync(host, world, TryAcquirePersistenceCaptureLease, action);

    private static async Task InvokeWhenReadyAsync(
        EngineHost host,
        World world,
        Func<World, bool> tryAcquireReadLease,
        Action<World> action)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(tryAcquireReadLease);
        ArgumentNullException.ThrowIfNull(action);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < s_timeout)
        {
            var remaining = s_timeout - stopwatch.Elapsed;
            await WaitForNextCommittedPublicationAsync(host, world, remaining).ConfigureAwait(false);
            var operationExecuted = false;
            await host.InvokeAsync(_ => {
                if (!tryAcquireReadLease(world))
                {
                    return;
                }

                action(world);
                operationExecuted = true;
            }).WaitAsync(s_timeout - stopwatch.Elapsed).ConfigureAwait(false);

            if (operationExecuted)
            {
                return;
            }
        }

        throw new TimeoutException("The hosted World did not provide a committed read boundary for the test operation.");
    }

    private static bool IsFrameReadBoundaryPending(string message)
        => message is "The World cannot begin a frame read lease during another operation."
            or "CPU extraction requires a committed World publication."
            or "CPU extraction requires the current canonical mutation revision to be committed.";

    private static bool IsPersistenceCaptureBoundaryPending(string message)
        => message is "The World cannot begin persistence capture during another operation."
                or "Persistence capture requires every admitted World owner request to be committed."
                or "Persistence capture requires an empty, quiescent Scene owner-request queue."
                or "Persistence capture requires a committed World publication."
                or "Persistence capture requires the current canonical mutation revision to be committed.";

    private static bool TryAcquireFrameReadLease(World world)
    {
        try
        {
            using var lease = world.AcquireFrameReadLease();
            return true;
        }
        catch (InvalidOperationException exception) when (IsFrameReadBoundaryPending(exception.Message))
        {
            return false;
        }
    }

    private static bool TryAcquirePersistenceCaptureLease(World world)
    {
        try
        {
            using var lease = world.AcquirePersistenceCaptureLease();
            return true;
        }
        catch (InvalidOperationException exception) when (IsPersistenceCaptureBoundaryPending(exception.Message))
        {
            return false;
        }
    }

    private static async Task WaitForNextCommittedPublicationAsync(
        EngineHost host,
        World world,
        TimeSpan timeout)
    {
        var signal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        WorldSubscription? subscription = null;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            var deferred = false;
            await host.InvokeAsync(_ => {
                try
                {
                    if (!world.TrySubscribe(
                        _ => {
                            if (subscription is not { } selectedSubscription)
                            {
                                throw new InvalidOperationException("The hosted World published before the test observer was registered.");
                            }

                            selectedSubscription.Dispose();

                            if (!signal.TrySetResult(true))
                            {
                                throw new InvalidOperationException("The hosted World invoked the one-shot test observer more than once.");
                            }
                        },
                        out subscription))
                    {
                        throw new InvalidOperationException("The hosted World has no available publication subscription slot.");
                    }
                }
                catch (InvalidOperationException exception) when (
                    exception.Message is "World subscriptions are unavailable during a precommit operation.")
                {
                    deferred = true;
                }
            }).WaitAsync(timeout - stopwatch.Elapsed).ConfigureAwait(false);

            if (subscription is not null)
            {
                _ = await signal.Task.WaitAsync(timeout - stopwatch.Elapsed).ConfigureAwait(false);
                return;
            }

            if (!deferred)
            {
                throw new InvalidOperationException("The hosted World failed to register the test publication observer.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1)).WaitAsync(timeout - stopwatch.Elapsed).ConfigureAwait(false);
        }

        throw new TimeoutException("The hosted World did not admit a publication observer.");
    }
}
