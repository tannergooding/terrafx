// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Content.Tools;

[SuppressMessage("Globalization", "CA1303", Justification = "The shader compiler is a repository-only developer CLI and its diagnostic and usage text is not localized.")]
internal static class Program
{
    private static readonly string[] s_usageLines = [
        "Usage:",
        "  TerraFX.ShaderCompiler shader compile --source <file> --output <directory> --entry <name> --stage <vertex|pixel|compute> --dxc <path> --vulkan-sdk <path>",
        "  TerraFX.ShaderCompiler --help",
    ];

    private static async Task<int> Main(string[] arguments)
    {
        if ((arguments.Length == 1) && StringComparer.Ordinal.Equals(arguments[0], "--help"))
        {
            await WriteUsageAsync().ConfigureAwait(false);
            return 0;
        }

        var isShaderCompile = (arguments.Length >= 2)
            && StringComparer.Ordinal.Equals(arguments[0], "shader")
            && StringComparer.Ordinal.Equals(arguments[1], "compile");

        if (!isShaderCompile)
        {
            await WriteUsageAsync().ConfigureAwait(false);
            return arguments.Length == 0 ? 0 : 2;
        }

        using var cancellationSource = new CancellationTokenSource();

        void Cancel(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        }

        Console.CancelKeyPress += Cancel;

        try
        {
            return await ShaderCompileCommand.RunCliAsync(
                arguments[2..],
                cancellationSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("The shader compiler operation was canceled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException
            or FormatException
            or IOException
            or InvalidOperationException
            or JsonException
            or PlatformNotSupportedException
            or TimeoutException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            await Console.Error.WriteLineAsync($"Shader compiler: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }

    private static Task WriteUsageAsync()
        => Console.Out.WriteLineAsync(string.Join(Environment.NewLine, s_usageLines));
}
