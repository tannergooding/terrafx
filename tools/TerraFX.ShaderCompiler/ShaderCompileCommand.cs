// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TerraFX.Content.Tools;

internal static class ShaderCompileCommand
{
    internal static async Task<ShaderCompileResult> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var debug = false;

        for (var index = 0; index < arguments.Length; index++)
        {
            var option = arguments[index];

            if (string.Equals(option, "--debug", StringComparison.Ordinal))
            {
                debug = true;
                continue;
            }

            if (!IsKnownOption(option))
            {
                throw new ArgumentException($"Unknown shader compiler option '{option}'.", nameof(arguments));
            }

            if (index + 1 >= arguments.Length)
            {
                throw new ArgumentException($"A value is missing for shader compiler option '{option}'.", nameof(arguments));
            }

            if (!IsRepeatableOption(option) && options.ContainsKey(option))
            {
                throw new ArgumentException($"Shader compiler option '{option}' was specified more than once.", nameof(arguments));
            }

            var value = arguments[++index];

            if (!options.TryGetValue(option, out var values))
            {
                values = [];
                options.Add(option, values);
            }

            values.Add(value);
        }

        var sourcePath = GetRequiredOption(options, "--source");
        var outputDirectory = GetRequiredOption(options, "--output");
        var entryPoint = GetRequiredOption(options, "--entry");
        var stage = GetRequiredOption(options, "--stage");
        var dxcPath = GetRequiredOption(options, "--dxc");
        var vulkanSdkPath = GetRequiredOption(options, "--vulkan-sdk");
        var includeRoots = GetOptionValues(options, "--include-root");
        var defines = new List<string>(GetOptionValues(options, "--define"));

        foreach (var defineList in GetOptionValues(options, "--define-list"))
        {
            defines.AddRange(defineList.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        var timeout = options.TryGetValue("--timeout-seconds", out var timeoutValues)
            ? ParseTimeout(timeoutValues)
            : BoundedToolProcess.DefaultTimeout;

        return await ShaderCompiler.CompileAsync(
            new ShaderCompileRequest(
                sourcePath,
                outputDirectory,
                entryPoint,
                stage,
                dxcPath,
                vulkanSdkPath,
                includeRoots,
                defines,
                debug,
                timeout),
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<int> RunCliAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var result = await RunAsync(arguments, cancellationToken).ConfigureAwait(false);

        foreach (var warning in result.Warnings)
        {
            await Console.Error.WriteLineAsync(warning, CancellationToken.None).ConfigureAwait(false);
        }

        await Console.Out.WriteLineAsync(result.DxilPath, CancellationToken.None).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(result.SpirvPath, CancellationToken.None).ConfigureAwait(false);
        await Console.Out.WriteLineAsync(result.Fingerprint, CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    private static IReadOnlyList<string> GetOptionValues(Dictionary<string, List<string>> options, string name)
        => options.TryGetValue(name, out var values) ? values : Array.Empty<string>();

    private static string GetRequiredOption(Dictionary<string, List<string>> options, string name)
    {
        var values = GetOptionValues(options, name);

        if ((values.Count != 1) || string.IsNullOrWhiteSpace(values[0]))
        {
            throw new ArgumentException($"Exactly one nonempty value is required for shader compiler option '{name}'.", nameof(options));
        }

        return values[0];
    }

    private static bool IsKnownOption(string option)
        => string.Equals(option, "--source", StringComparison.Ordinal)
        || string.Equals(option, "--output", StringComparison.Ordinal)
        || string.Equals(option, "--entry", StringComparison.Ordinal)
        || string.Equals(option, "--stage", StringComparison.Ordinal)
        || string.Equals(option, "--dxc", StringComparison.Ordinal)
        || string.Equals(option, "--vulkan-sdk", StringComparison.Ordinal)
        || string.Equals(option, "--timeout-seconds", StringComparison.Ordinal)
        || string.Equals(option, "--define-list", StringComparison.Ordinal)
        || IsRepeatableOption(option);

    private static bool IsRepeatableOption(string option)
        => string.Equals(option, "--include-root", StringComparison.Ordinal)
        || string.Equals(option, "--define", StringComparison.Ordinal);

    private static TimeSpan ParseTimeout(List<string> values)
    {
        if ((values.Count != 1)
            || !double.TryParse(values[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds)
            || (seconds <= 0.0)
            || (seconds >= TimeSpan.MaxValue.TotalSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(values), "The shader-tool timeout must be a positive finite duration that fits in TimeSpan.");
        }

        var timeout = TimeSpan.FromSeconds(seconds);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(values), "The shader-tool timeout must be at least one TimeSpan tick.");
        }

        return timeout;
    }
}
