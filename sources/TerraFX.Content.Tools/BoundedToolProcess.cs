// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Content.Tools;

internal static class BoundedToolProcess
{
    internal const int MaximumCapturedBytes = 8 * 1024 * 1024;
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    internal static async Task<ToolProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        var startInfo = new ProcessStartInfo {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentVariables is not null)
        {
            foreach (var environmentVariable in environmentVariables)
            {
                startInfo.Environment[environmentVariable.Key] = environmentVariable.Value;
            }
        }

        using var process = new Process {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"The tool process '{executablePath}' did not start.");
        }

        var budget = new CapturedOutputBudget(MaximumCapturedBytes);
        var standardOutputTask = ReadOutputAsync(process.StandardOutput.BaseStream, budget);
        var standardErrorTask = ReadOutputAsync(process.StandardError.BaseStream, budget);
        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var standardOutputCompleted = false;
        var standardErrorCompleted = false;

        while (!exitTask.IsCompleted)
        {
            var pendingTasks = new List<Task> { exitTask, timeoutTask };

            if (!standardOutputCompleted)
            {
                pendingTasks.Add(standardOutputTask);
            }

            if (!standardErrorCompleted)
            {
                pendingTasks.Add(standardErrorTask);
            }

            var completedTask = await Task.WhenAny(pendingTasks).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                var canceled = cancellationToken.IsCancellationRequested;
                await TerminateAsync(process, exitTask).ConfigureAwait(false);

                if (canceled)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                throw new TimeoutException($"The tool process '{executablePath}' exceeded {timeout}.");
            }

            if (completedTask == standardOutputTask)
            {
                if (standardOutputTask.IsFaulted)
                {
                    await TerminateAsync(process, exitTask).ConfigureAwait(false);
                    _ = await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
                    throw new InvalidOperationException("The standard-output reader failed without an exception.");
                }

                standardOutputCompleted = true;
            }
            else if (completedTask == standardErrorTask)
            {
                if (standardErrorTask.IsFaulted)
                {
                    await TerminateAsync(process, exitTask).ConfigureAwait(false);
                    _ = await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
                    throw new InvalidOperationException("The standard-error reader failed without an exception.");
                }

                standardErrorCompleted = true;
            }
        }

        await exitTask.ConfigureAwait(false);
        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        return new ToolProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static async Task<string> ReadOutputAsync(Stream stream, CapturedOutputBudget budget)
    {
        var buffer = new byte[4096];
        var output = new ArrayBufferWriter<byte>();

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer).ConfigureAwait(false);

            if (bytesRead == 0)
            {
                break;
            }

            budget.Reserve(bytesRead);
            buffer.AsSpan(0, bytesRead).CopyTo(output.GetSpan(bytesRead));
            output.Advance(bytesRead);
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
            .GetString(output.WrittenSpan);
    }

    private static async Task TerminateAsync(Process process, Task exitTask)
    {
        if (!process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }
        }

        await exitTask.ConfigureAwait(false);
    }

    private sealed class CapturedOutputBudget
    {
        private int _capturedBytes;
        private readonly int _maximumBytes;

        internal CapturedOutputBudget(int maximumBytes)
        {
            _maximumBytes = maximumBytes;
        }

        internal void Reserve(int byteCount)
        {
            var capturedBytes = Volatile.Read(ref _capturedBytes);

            while (true)
            {
                if (byteCount > (_maximumBytes - capturedBytes))
                {
                    throw new InvalidDataException($"ToolOutputLimitExceeded: the process exceeded the {_maximumBytes}-byte aggregate raw-output limit.");
                }

                var updatedBytes = capturedBytes + byteCount;
                var observedBytes = Interlocked.CompareExchange(ref _capturedBytes, updatedBytes, capturedBytes);

                if (observedBytes == capturedBytes)
                {
                    return;
                }

                capturedBytes = observedBytes;
            }
        }
    }
}
