// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using TerraFX;

namespace TerraFX.Entities;

/// <summary>Provides one callback-scoped result channel for decoding attachment state.</summary>
/// <typeparam name="TAuthor">The immutable symbolic author type.</typeparam>
public sealed class WorldAttachmentReadBinding<TAuthor>
    where TAuthor : notnull
{
    private Exception? _failure;
    private bool _isActive = true;
    private bool _isIssued;
    private readonly int _ownerThreadId;
    private WorldAttachmentReadResult<TAuthor>? _result;
    private readonly ContentSchemaReference _schema;
    private readonly nint _sourceByteLength;

    internal WorldAttachmentReadBinding(ContentSchemaReference schema, nint sourceByteLength)
    {
        if ((schema.SchemaId == Guid.Empty)
            || (schema.Major <= 0)
            || (schema.Minor < 0)
            || (schema.Scope != ContentSchemaScope.Content))
        {
            throw new ArgumentException("An attachment read binding requires an exact Content schema tuple.", nameof(schema));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sourceByteLength);
        _schema = schema;
        _sourceByteLength = sourceByteLength;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>Gets the exact AttachmentState schema tuple.</summary>
    public ContentSchemaReference Schema => _schema;

    /// <summary>Gets the exact source extent available to this callback.</summary>
    public nint SourceByteLength => _sourceByteLength;

    internal Exception? Failure => _failure;

    private static string FormatPath(ComponentFieldPath path)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var segment in path.Segments)
        {
            if (builder.Length != 0)
            {
                _ = builder.Append('/');
            }

            _ = builder.Append((int)segment.Kind);
            _ = builder.Append(':');
            _ = builder.Append(segment.Value);
        }

        return builder.ToString();
    }

    /// <summary>Completes the read with one fully consumed immutable author value.</summary>
    /// <param name="author">The nonnull decoded author value.</param>
    /// <param name="consumedBytes">The exact number of source bytes consumed.</param>
    /// <returns>The complete read result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="author"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The binding is invalid, the extent is incomplete, or a result was already issued.</exception>
    public WorldAttachmentReadResult<TAuthor> Complete([DisallowNull] in TAuthor author, nint consumedBytes)
    {
        EnsureCanIssue();

        try
        {
            ArgumentNullException.ThrowIfNull(author);

            if (consumedBytes != _sourceByteLength)
            {
                throw new InvalidOperationException("A complete attachment state read must consume the exact source extent.");
            }

            var result = new WorldAttachmentReadResult<TAuthor>(WorldAttachmentReadStatus.Read, author, consumedBytes, []);
            _result = result;
            return result;
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    /// <summary>Rejects malformed or unsupported attachment state with a bounded diagnostic.</summary>
    /// <param name="kind">The exact malformed-input category.</param>
    /// <param name="sourceOffset">The byte offset within the exact source extent.</param>
    /// <param name="path">The optional selected schema field path.</param>
    /// <param name="message">The nonempty bounded human-readable diagnostic.</param>
    /// <returns>The rejected read result.</returns>
    /// <exception cref="ArgumentException"><paramref name="message"/> is empty or <paramref name="path"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> or <paramref name="sourceOffset"/> is invalid.</exception>
    /// <exception cref="InvalidOperationException">The binding is invalid or a result was already issued.</exception>
    public WorldAttachmentReadResult<TAuthor> Reject(
        WorldAttachmentReadFailureKind kind,
        nint sourceOffset,
        ComponentFieldPath? path,
        string message)
    {
        EnsureCanIssue();

        try
        {
            if (kind is < WorldAttachmentReadFailureKind.MalformedRecord or > WorldAttachmentReadFailureKind.ConsumptionMismatch)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            ArgumentOutOfRangeException.ThrowIfNegative(sourceOffset);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceOffset, _sourceByteLength);
            ArgumentNullException.ThrowIfNull(message);

            if (message.Length is 0 or > 4096)
            {
                throw new ArgumentException("An attachment state diagnostic message must be nonempty and bounded.", nameof(message));
            }

            var context = ImmutableArray.CreateBuilder<DiagnosticContextEntry>(3);
            context.Add(new DiagnosticContextEntry("schemaId", _schema.SchemaId.ToString("D")));
            context.Add(new DiagnosticContextEntry("sourceOffset", sourceOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (path is not null)
            {
                context.Add(new DiagnosticContextEntry("fieldPath", FormatPath(path)));
            }

            var diagnostic = new Diagnostic(
                $"world.attachment.read.{kind}",
                DiagnosticSeverity.Error,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "WorldAttachmentState.Read",
                null,
                message,
                context.ToImmutable());
            var result = new WorldAttachmentReadResult<TAuthor>(WorldAttachmentReadStatus.Rejected, default, 0, [diagnostic]);
            _result = result;
            return result;
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    /// <summary>Rejects a source using an existing Core EBIN structural diagnostic.</summary>
    /// <param name="status">The non-complete Core read status.</param>
    /// <param name="diagnostic">The exact structural diagnostic.</param>
    /// <returns>The rejected read result.</returns>
    /// <exception cref="ArgumentException"><paramref name="status"/> or <paramref name="diagnostic"/> is inconsistent.</exception>
    /// <exception cref="InvalidOperationException">The binding is invalid or a result was already issued.</exception>
    public WorldAttachmentReadResult<TAuthor> Reject(EbinReadStatus status, EbinReadDiagnostic? diagnostic)
    {
        EnsureCanIssue();

        try
        {
            if (status is not EbinReadStatus.NeedMoreData and not EbinReadStatus.InvalidRecord and not EbinReadStatus.UnsupportedSchema)
            {
                throw new ArgumentException("An attachment state read can preserve only a non-complete Core EBIN rejection.", nameof(status));
            }

            if (diagnostic is not EbinReadDiagnostic value)
            {
                throw new ArgumentException("A Core EBIN diagnostic is required for a rejected read.", nameof(diagnostic));
            }

            if ((value.SchemaId != _schema.SchemaId)
                || string.IsNullOrEmpty(value.Category)
                || (value.Offset > (ulong)_sourceByteLength))
            {
                throw new ArgumentException("The Core EBIN diagnostic does not match this attachment schema and source extent.", nameof(diagnostic));
            }

            var diagnosticContext = ImmutableArray.Create(
                new DiagnosticContextEntry("schemaId", value.SchemaId.ToString("D")),
                new DiagnosticContextEntry("tag", value.Tag.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("offset", value.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("claim", value.Claim.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new DiagnosticContextEntry("limit", value.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            var resultDiagnostic = new Diagnostic(
                $"world.attachment.ebin.{value.Category}",
                DiagnosticSeverity.Error,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "WorldAttachmentState.Read",
                null,
                value.Category,
                diagnosticContext);
            var result = new WorldAttachmentReadResult<TAuthor>(WorldAttachmentReadStatus.Rejected, default, 0, [resultDiagnostic]);
            _result = result;
            return result;
        }
        catch (Exception exception)
        {
            throw RecordFailure(exception);
        }
    }

    internal void Revoke() => _isActive = false;

    internal WorldAttachmentReadResult<TAuthor> ValidateComplete(WorldAttachmentReadResult<TAuthor>? result)
    {
        if (!_isActive)
        {
            throw RecordFailure(new InvalidOperationException("The World attachment read binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw RecordFailure(new InvalidOperationException("The World attachment read binding is restricted to its issuing thread."));
        }

        if (_failure is { } failure)
        {
            throw failure;
        }

        if (!_isIssued || (_result is null) || !ReferenceEquals(result, _result))
        {
            throw RecordFailure(new InvalidOperationException("The attachment provider did not return this callback's exact issued read result."));
        }

        return _result;
    }

    private void EnsureCanIssue()
    {
        if (!_isActive)
        {
            throw RecordFailure(new InvalidOperationException("The World attachment read binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw RecordFailure(new InvalidOperationException("The World attachment read binding is restricted to its issuing thread."));
        }

        if (_isIssued)
        {
            throw RecordFailure(new InvalidOperationException("An attachment read callback can issue only one result."));
        }

        _isIssued = true;
    }

    private Exception RecordFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _failure ??= exception;
        return _failure;
    }
}
