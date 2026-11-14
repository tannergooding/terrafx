// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Content.Tools;

[SuppressMessage("Globalization", "CA1303", Justification = "Content.Tools is a repository-only developer CLI and its diagnostic and usage text is not localized.")]
internal static class Program
{
    private const string CompileDxilCommand = "compile-m04-dxil";
    private const string CompileShaderCommand = "compile-m04-shader";
    private const string CookStaticMeshCommand = "cook-static-mesh";
    private const string VerifyDxcCommand = "verify-dxc";
    private static readonly string[] s_usageLines = [
        "Usage:",
        "  TerraFX.Content.Tools verify-dxc",
        "  TerraFX.Content.Tools compile-m04-dxil <new-output-directory>",
        "  TerraFX.Content.Tools compile-m04-shader <new-output-directory>",
        "  TerraFX.Content.Tools cook-static-mesh <glb|obj> --source <path> --profile-record <path> --material-record <path> --mesh-id <id> --material-id <id> --output <package>",
        "  OBJ additionally requires --obj-convention rh-y-up-z-forward-meters",
        "  TerraFX.Content.Tools shader compile --source <file> --output <directory> --entry <name> --stage <vertex|pixel|compute> --dxc <path> --vulkan-sdk <path>",
        "  TerraFX.Content.Tools --help",
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
        var isStaticMeshCook = (arguments.Length >= 2)
            && StringComparer.Ordinal.Equals(arguments[0], CookStaticMeshCommand);
        var isM04Compile = (arguments.Length == 2)
            && (StringComparer.Ordinal.Equals(arguments[0], CompileShaderCommand)
                || StringComparer.Ordinal.Equals(arguments[0], CompileDxilCommand));
        var isDxcVerification = (arguments.Length == 1)
            && StringComparer.Ordinal.Equals(arguments[0], VerifyDxcCommand);

        if (!isShaderCompile && !isStaticMeshCook && !isM04Compile && !isDxcVerification)
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
            if (isStaticMeshCook)
            {
                return await StaticMeshCookCommand.RunCliAsync(
                    arguments[1..],
                    cancellationSource.Token).ConfigureAwait(false);
            }

            if (isShaderCompile)
            {
                return await ShaderCompileCommand.RunCliAsync(
                    arguments[2..],
                    cancellationSource.Token).ConfigureAwait(false);
            }

            if (StringComparer.Ordinal.Equals(arguments[0], CompileShaderCommand))
            {
                await M04ShaderFixtureCompiler.CompileAsync(
                    arguments[1],
                    cancellationSource.Token).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(
                    $"Compiled and validated the M04 offscreen triangle shader fixture to '{Path.GetFullPath(arguments[1])}'.")
                    .ConfigureAwait(false);
            }
            else if (StringComparer.Ordinal.Equals(arguments[0], CompileDxilCommand))
            {
                await M04ShaderFixtureCompiler.CompileDxilAsync(
                    arguments[1],
                    cancellationSource.Token).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(
                    $"Compiled and reflected the M04 DXIL fixture to '{Path.GetFullPath(arguments[1])}'.")
                    .ConfigureAwait(false);
            }
            else
            {
                await M04ShaderFixtureCompiler.VerifyDxcAsync(cancellationSource.Token).ConfigureAwait(false);
                await Console.Out.WriteLineAsync(
                    "The pinned Windows x64 DXC package files and executable version are valid.")
                    .ConfigureAwait(false);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("The Content.Tools operation was canceled.").ConfigureAwait(false);
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException
            or FormatException
            or IOException
            or InvalidOperationException
            or NotSupportedException
            or JsonException
            or PlatformNotSupportedException
            or TimeoutException
            or UnauthorizedAccessException
            or Win32Exception)
        {
            await Console.Error.WriteLineAsync($"Content.Tools: {exception.Message}").ConfigureAwait(false);
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
