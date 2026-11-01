// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Content.Tools;

internal static class ToolProcessRunner
{
    internal static async Task<ToolProcessResult> RunAsync(
        string executablePath,
        string workingDirectory,
        ReadOnlyMemory<string> arguments,
        TimeSpan timeout,
        int maximumCapturedOutputBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCapturedOutputBytes);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        for (var index = 0; index < arguments.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(arguments.Span[index]);
            startInfo.ArgumentList.Add(arguments.Span[index]);
        }

        using var process = new Process {
            StartInfo = startInfo,
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start offline tool '{executablePath}'.");
        }

        var outputBudget = new OutputBudget(maximumCapturedOutputBytes);
        var standardOutputTask = CaptureAsync(process.StandardOutput.BaseStream, outputBudget);
        var standardErrorTask = CaptureAsync(process.StandardError.BaseStream, outputBudget);
        var processWaitTask = process.WaitForExitAsync(CancellationToken.None);
        var completionTask = Task.WhenAll(processWaitTask, standardOutputTask, standardErrorTask);
        var timeoutTask = Task.Delay(timeout, cancellationToken);

        try
        {
            var completedTask = await Task.WhenAny(completionTask, timeoutTask, outputBudget.LimitExceeded).ConfigureAwait(false);

            if (completedTask == outputBudget.LimitExceeded)
            {
                KillProcessTree(process);
                await WaitForExitAndCaptureAsync(processWaitTask, standardOutputTask, standardErrorTask).ConfigureAwait(false);
                throw new IOException($"Offline tool output exceeded the {outputBudget.MaximumBytes}-byte combined limit.");
            }

            if ((completedTask == timeoutTask) && !completionTask.IsCompleted)
            {
                KillProcessTree(process);
                await WaitForExitAndCaptureAsync(processWaitTask, standardOutputTask, standardErrorTask).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"Offline tool '{executablePath}' exceeded its {timeout.TotalSeconds:0}-second timeout.");
            }

            await completionTask.ConfigureAwait(false);

            if (outputBudget.LimitExceeded.IsCompleted)
            {
                throw new IOException($"Offline tool output exceeded the {outputBudget.MaximumBytes}-byte combined limit.");
            }
        }
        catch
        {
            if (!process.HasExited)
            {
                KillProcessTree(process);
            }

            await WaitForExitAndCaptureAsync(processWaitTask, standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }

        var standardOutput = Encoding.UTF8.GetString(await standardOutputTask.ConfigureAwait(false));
        var standardError = Encoding.UTF8.GetString(await standardErrorTask.ConfigureAwait(false));

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Offline tool '{executablePath}' exited with code {process.ExitCode}.{Environment.NewLine}{standardError}{standardOutput}");
        }

        return new ToolProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private static async Task<byte[]> CaptureAsync(Stream source, OutputBudget outputBudget)
    {
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(8192);

        try
        {
            while (true)
            {
                var bytesRead = await source.ReadAsync(buffer.AsMemory(), CancellationToken.None).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    return output.ToArray();
                }

                if (!outputBudget.TryReserve(bytesRead))
                {
                    return output.ToArray();
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void KillProcessTree(Process process)
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
            catch (Win32Exception) when (process.HasExited)
            {
            }
        }
    }

    private static async Task WaitForExitAndCaptureAsync(
        Task processWaitTask,
        Task<byte[]> standardOutputTask,
        Task<byte[]> standardErrorTask)
    {
        await processWaitTask.ConfigureAwait(false);
        _ = await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
    }

    internal readonly record struct ToolProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class OutputBudget
    {
        private int _capturedBytes;
        private readonly TaskCompletionSource _limitExceeded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal OutputBudget(int maximumBytes)
        {
            MaximumBytes = maximumBytes;
        }

        internal Task LimitExceeded => _limitExceeded.Task;

        internal int MaximumBytes { get; }

        internal bool TryReserve(int byteCount)
        {
            while (true)
            {
                var currentBytes = Volatile.Read(ref _capturedBytes);

                if (byteCount > MaximumBytes - currentBytes)
                {
                    _ = _limitExceeded.TrySetResult();
                    return false;
                }

                if (Interlocked.CompareExchange(ref _capturedBytes, currentBytes + byteCount, currentBytes) == currentBytes)
                {
                    return true;
                }
            }
        }
    }
}
