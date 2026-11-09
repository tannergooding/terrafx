// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace TerraFX.Platform.Tests;

[SupportedOSPlatform("windows10.0.22000")]
internal static unsafe partial class WindowsClipboardTestProcess
{
    internal const string ChildMarker = "TERRAFX_WINDOWS_CLIPBOARD_STATION_CHILD";
    internal const string DesktopMarker = "TERRAFX_WINDOWS_CLIPBOARD_STATION_DESKTOP";
    internal const string StationMarker = "TERRAFX_WINDOWS_CLIPBOARD_STATION_NAME";

    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint StartfUseShowWindow = 0x00000001;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;

    internal static string FindRepositoryRoot(string startDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(startDirectory);

        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TerraFX.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The TerraFX repository root could not be located from the test output directory.");
    }

    internal static void RunClipboardChild(
        string projectPath,
        string workingDirectory,
        string configuration,
        string windowStationName,
        string desktopName)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);
        ArgumentException.ThrowIfNullOrEmpty(configuration);
        ArgumentException.ThrowIfNullOrEmpty(windowStationName);
        ArgumentException.ThrowIfNullOrEmpty(desktopName);

        var resultFileName = $"windows-clipboard-child-{Guid.NewGuid():N}.trx";
        var resultsDirectory = Path.Combine(workingDirectory, "artifacts", "tst", configuration);
        _ = Directory.CreateDirectory(resultsDirectory);
        var filter = "FullyQualifiedName=TerraFX.Platform.Tests.WindowsClipboardIsolationTests.ReadsAndWritesClipboardOnTestOwnedStationInChild";
        var commandLine = BuildCommandLine(projectPath, configuration, filter, resultsDirectory, resultFileName);
        var desktopNameBuffer = string.Concat(windowStationName, "\\", desktopName, '\0').ToCharArray();
        var commandLineBuffer = string.Concat(commandLine, '\0').ToCharArray();
        var currentDirectoryBuffer = string.Concat(workingDirectory, '\0').ToCharArray();
        var environmentBuffer = BuildEnvironmentBlock(windowStationName, desktopName);
        var startupInfo = new StartupInfo {
            cb = checked((uint)sizeof(StartupInfo)),
            dwFlags = StartfUseShowWindow,
            wShowWindow = 0,
        };
        var processInfo = default(ProcessInformation);

        fixed (char* desktop = desktopNameBuffer)
        fixed (char* commandLinePointer = commandLineBuffer)
        fixed (char* currentDirectory = currentDirectoryBuffer)
        fixed (char* environment = environmentBuffer)
        {
            startupInfo.lpDesktop = (nint)desktop;

            if (!CreateProcessNative(
                0,
                commandLinePointer,
                0,
                0,
                false,
                CreateNoWindow | CreateUnicodeEnvironment,
                environment,
                currentDirectory,
                &startupInfo,
                &processInfo))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The isolated clipboard test process could not be started.");
            }
        }

        using var processHandle = new SafeProcessHandle(processInfo.hProcess, ownsHandle: true);
        using var threadHandle = new SafeWaitHandle(processInfo.hThread, ownsHandle: true);
        var waitResult = WaitForSingleObjectNative(processHandle, milliseconds: 300_000);

        if (waitResult == WaitTimeout)
        {
            if (!TerminateProcessNative(processHandle, exitCode: 1))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The timed-out clipboard test process could not be terminated.");
            }

            _ = WaitForSingleObjectNative(processHandle, Infinite);
            throw new TimeoutException("The isolated Windows clipboard test process exceeded five minutes.");
        }

        if (waitResult == WaitFailed)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Waiting for the isolated clipboard test process failed.");
        }

        if (waitResult != WaitObject0)
        {
            throw new InvalidOperationException($"Waiting for the isolated clipboard test process returned unexpected value {waitResult}.");
        }

        if (!GetExitCodeProcessNative(processHandle, out var exitCode))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The isolated clipboard test process exit code could not be read.");
        }

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"The isolated Windows clipboard child test exited with code {exitCode}; inspect {Path.Combine(resultsDirectory, resultFileName)}.");
        }
    }

    private static string BuildCommandLine(
        string projectPath,
        string configuration,
        string filter,
        string resultsDirectory,
        string resultFileName)
        => string.Join(
            " ",
            "dotnet",
            "test",
            QuoteArgument(projectPath),
            "--no-build",
            "--no-restore",
            "--configuration",
            configuration,
            "--filter",
            QuoteArgument(filter),
            "--logger",
            QuoteArgument($"trx;LogFileName={resultFileName}"),
            "--results-directory",
            QuoteArgument(resultsDirectory),
            "-m:1",
            "-nr:false");

    private static char[] BuildEnvironmentBlock(string windowStationName, string desktopName)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
            {
                variables[key] = entry.Value?.ToString() ?? string.Empty;
            }
        }

        variables[ChildMarker] = "1";
        variables[DesktopMarker] = desktopName;
        variables[StationMarker] = windowStationName;
        var builder = new StringBuilder();

        foreach (var variable in variables)
        {
            _ = builder.Append(variable.Key)
                .Append('=')
                .Append(variable.Value)
                .Append('\0');
        }

        _ = builder.Append('\0');
        return builder.ToString().ToCharArray();
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessNative(
        nint applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        char* environment,
        char* currentDirectory,
        StartupInfo* startupInfo,
        ProcessInformation* processInformation);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "GetExitCodeProcess", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcessNative(SafeProcessHandle process, out uint exitCode);

    private static string QuoteArgument(string value)
        => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "TerminateProcess", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcessNative(SafeProcessHandle process, uint exitCode);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
    private static partial uint WaitForSingleObjectNative(SafeHandle handle, uint milliseconds);

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage(
        "Design",
        "TFX0002:Order type members by category, staticness, accessibility, and name using ordinal comparison",
        Justification = "The field order is the native PROCESS_INFORMATION layout.")]
    private struct ProcessInformation
    {
        internal nint hProcess;
        internal nint hThread;
        internal uint dwProcessId;
        internal uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    [SuppressMessage(
        "Design",
        "TFX0002:Order type members by category, staticness, accessibility, and name using ordinal comparison",
        Justification = "The field order is the native STARTUPINFOW layout.")]
    private struct StartupInfo
    {
        internal uint cb;
        internal nint lpReserved;
        internal nint lpDesktop;
        internal nint lpTitle;
        internal uint dwX;
        internal uint dwY;
        internal uint dwXSize;
        internal uint dwYSize;
        internal uint dwXCountChars;
        internal uint dwYCountChars;
        internal uint dwFillAttribute;
        internal uint dwFlags;
        internal ushort wShowWindow;
        internal ushort cbReserved2;
        internal nint lpReserved2;
        internal nint hStdInput;
        internal nint hStdOutput;
        internal nint hStdError;
    }
}
