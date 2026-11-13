// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TerraFX.Content.Tools;

internal sealed class ShaderSourceClosure
{
    internal const int MaximumIncludeDepth = 64;
    internal const int MaximumSourceBytes = 4 * 1024 * 1024;

    private static readonly Regex s_includeDirective = new Regex(
        @"^[\t ]*#[\t ]*include[\t ]*[<""](?<path>[^>""]+)[>""][\t ]*(?://.*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);
    private static readonly Regex s_includeToken = new Regex(
        @"^[\t ]*#[\t ]*include\b",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private readonly List<ShaderSourceFile> _files;
    private readonly List<string> _roots;
    private long _sourceBytes;

    private ShaderSourceClosure(List<ShaderSourceFile> files, List<string> roots)
    {
        _files = files;
        _roots = roots;
    }

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal IReadOnlyList<ShaderSourceFile> Files => _files;

    internal IReadOnlyList<string> Roots => _roots;

    internal static ShaderSourceClosure Load(string sourcePath, IReadOnlyList<string> includeRoots)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentNullException.ThrowIfNull(includeRoots);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceDirectory = Path.GetDirectoryName(fullSourcePath)
            ?? throw new ArgumentException("The shader source has no parent directory.", nameof(sourcePath));
        List<string> roots = [Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory))];

        foreach (var includeRoot in includeRoots)
        {
            ArgumentException.ThrowIfNullOrEmpty(includeRoot);
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(includeRoot));

            if (!roots.Contains(normalizedRoot, PathComparer))
            {
                roots.Add(normalizedRoot);
            }
        }

        var closure = new ShaderSourceClosure([], roots);
        closure.AddFile(fullSourcePath, depth: 0, activePaths: new HashSet<string>(capacity: 0, comparer: PathComparer));
        return closure;
    }

    private static void EnsureContained(string root, string path, string parameterName)
    {
        var relativePath = Path.GetRelativePath(root, path);

        if (Path.IsPathRooted(relativePath)
            || (relativePath == "..")
            || relativePath.StartsWith(string.Concat("..", Path.DirectorySeparatorChar), PathComparison)
            || relativePath.StartsWith(string.Concat("..", Path.AltDirectorySeparatorChar), PathComparison))
        {
            throw new InvalidDataException($"The shader path '{path}' is outside its declared root '{root}'.");
        }
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        var volumeRoot = Path.GetPathRoot(root)
            ?? throw new InvalidDataException($"The shader include root '{root}' is not fully qualified.");
        var current = volumeRoot;
        var relativeRoot = Path.GetRelativePath(volumeRoot, root);
        var relativePath = Path.GetRelativePath(root, path);
        var segments = relativeRoot
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Concat(relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            var attributes = File.GetAttributes(current);

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"The shader source path contains a reparse point: '{current}'.");
            }
        }
    }

    private static IEnumerable<string> ReadIncludeLines(string source)
    {
        using var reader = new StringReader(source);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            if (!s_includeToken.IsMatch(line))
            {
                continue;
            }

            var match = s_includeDirective.Match(line);

            if (!match.Success)
            {
                throw new InvalidDataException("Shader includes must use a literal quoted or angle-bracket path.");
            }

            yield return match.Groups["path"].Value;
        }
    }

    private static byte[] ReadSourceBytes(string path, long maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException($"The shader source closure exceeds {MaximumSourceBytes} bytes.");
        }

        using var buffer = new MemoryStream(checked((int)stream.Length));
        var bytes = new byte[8192];

        while (true)
        {
            var bytesRead = stream.Read(bytes, 0, bytes.Length);

            if (bytesRead == 0)
            {
                break;
            }

            if (bytesRead > maximumBytes - buffer.Length)
            {
                throw new InvalidDataException($"The shader source closure exceeds {MaximumSourceBytes} bytes.");
            }

            buffer.Write(bytes, 0, bytesRead);
        }

        return buffer.ToArray();
    }

    internal string ComputeFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBuffer = stackalloc byte[sizeof(int)];

        foreach (var file in _files)
        {
            var logicalPath = string.Concat(file.RootIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), "/", file.RelativePath.Replace('\\', '/'));
            var pathBytes = Encoding.UTF8.GetBytes(logicalPath);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, pathBytes.Length);
            hash.AppendData(lengthBuffer);
            hash.AppendData(pathBytes);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, file.Contents.Length);
            hash.AppendData(lengthBuffer);
            hash.AppendData(file.Contents);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal void CopyTo(string destinationDirectory)
    {
        for (var index = 0; index < _roots.Count; index++)
        {
            _ = Directory.CreateDirectory(Path.Combine(destinationDirectory, index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        foreach (var file in _files)
        {
            var root = Path.Combine(destinationDirectory, file.RootIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var destinationPath = Path.GetFullPath(Path.Combine(root, file.RelativePath));
            EnsureContained(root, destinationPath, nameof(destinationDirectory));
            var directory = Path.GetDirectoryName(destinationPath)
                ?? throw new InvalidOperationException("The staged shader source has no parent directory.");
            _ = Directory.CreateDirectory(directory);
            File.WriteAllBytes(destinationPath, file.Contents);
        }
    }

    private void AddFile(string path, int depth, HashSet<string> activePaths)
    {
        var fullPath = Path.GetFullPath(path);

        if (activePaths.Contains(fullPath))
        {
            throw new InvalidDataException($"The shader include graph contains a cycle at '{fullPath}'.");
        }

        if (_files.Any(file => PathComparer.Equals(file.FullPath, fullPath)))
        {
            return;
        }

        if (depth > MaximumIncludeDepth)
        {
            throw new InvalidDataException($"The shader include graph exceeds {MaximumIncludeDepth} levels.");
        }

        var rootIndex = FindContainingRoot(fullPath);
        var relativePath = Path.GetRelativePath(_roots[rootIndex], fullPath);
        EnsureContained(_roots[rootIndex], fullPath, nameof(path));
        EnsureNoReparsePoints(_roots[rootIndex], fullPath);
        var fileInfo = new FileInfo(fullPath);

        if (fileInfo.Length > MaximumSourceBytes - _sourceBytes)
        {
            throw new InvalidDataException($"The shader source closure exceeds {MaximumSourceBytes} bytes.");
        }

        var contents = ReadSourceBytes(fullPath, MaximumSourceBytes - _sourceBytes);
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(contents);
        _sourceBytes += contents.LongLength;
        var file = new ShaderSourceFile(fullPath, relativePath, rootIndex, contents);
        _files.Add(file);

        _ = activePaths.Add(fullPath);

        foreach (var line in ReadIncludeLines(text))
        {
            var includePath = ResolveInclude(fullPath, line);
            AddFile(includePath, depth + 1, activePaths);
        }

        _ = activePaths.Remove(fullPath);
    }

    private int FindContainingRoot(string path)
    {
        for (var index = 0; index < _roots.Count; index++)
        {
            var relativePath = Path.GetRelativePath(_roots[index], path);

            if (!Path.IsPathRooted(relativePath)
                && (relativePath != "..")
                && !relativePath.StartsWith(string.Concat("..", Path.DirectorySeparatorChar), PathComparison)
                && !relativePath.StartsWith(string.Concat("..", Path.AltDirectorySeparatorChar), PathComparison))
            {
                return index;
            }
        }

        throw new InvalidDataException($"The shader source '{path}' is outside all declared include roots.");
    }

    private string ResolveInclude(string includingFile, string includePath)
    {
        if (Path.IsPathRooted(includePath))
        {
            throw new InvalidDataException($"The shader include '{includePath}' must be relative to a declared root.");
        }

        var includingDirectory = Path.GetDirectoryName(includingFile)
            ?? throw new InvalidOperationException("The including shader file has no parent directory.");
        List<string> candidates = [Path.GetFullPath(Path.Combine(includingDirectory, includePath))];

        foreach (var root in _roots)
        {
            candidates.Add(Path.GetFullPath(Path.Combine(root, includePath)));
        }

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            _ = FindContainingRoot(candidate);
            return candidate;
        }

        throw new FileNotFoundException($"The shader include '{includePath}' was not found under a declared root.", includePath);
    }
}
