// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using TerraFX;

namespace TerraFX.Entities;

/// <summary>Provides scoped one-result admission for one resource codec read.</summary>
/// <typeparam name="TAuthor">The symbolic author value type.</typeparam>
public sealed class WorldResourceReadBinding<TAuthor>
    where TAuthor : notnull
{
    private const int MaximumMessageLength = 4096;

    private Exception? _failure;
    private bool _isActive = true;
    private readonly int _ownerThreadId;
    private readonly int _partitionCount;
    private readonly Guid _resourceId;
    private bool _resultIssued;
    private readonly ContentSchemaReference _schema;
    private readonly nint _sourceByteLength;

    internal WorldResourceReadBinding(
        Guid resourceId,
        ContentSchemaReference schema,
        int partitionCount,
        nint sourceByteLength)
    {
        if (resourceId == Guid.Empty)
        {
            throw new ArgumentException("A resource read binding requires a nonempty resource identity.", nameof(resourceId));
        }

        if ((schema.SchemaId == Guid.Empty)
            || (schema.Major <= 0)
            || (schema.Minor < 0))
        {
            throw new ArgumentException("A resource read binding requires an exact valid schema tuple.", nameof(schema));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partitionCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceByteLength);

        _resourceId = resourceId;
        _schema = schema;
        _partitionCount = partitionCount;
        _sourceByteLength = sourceByteLength;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>Gets the complete resource partition count.</summary>
    public int PartitionCount => _partitionCount;

    /// <summary>Gets the resource identity.</summary>
    public Guid ResourceId => _resourceId;

    /// <summary>Gets the exact resource state schema tuple selected for this read.</summary>
    public ContentSchemaReference Schema => _schema;

    /// <summary>Gets the exact length of the scoped source record.</summary>
    public nint SourceByteLength => _sourceByteLength;

    internal Exception? Failure => _failure;

    private static Diagnostic CreateDiagnostic(
        string code,
        string message,
        ImmutableArray<DiagnosticContextEntry> context)
        => new(
            code,
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
            "WorldResourceStateCodec.Read",
            null,
            message,
            context);

    private static string FormatPath(ComponentFieldPath path)
    {
        var builder = new StringBuilder();

        foreach (var segment in path.Segments)
        {
            if (builder.Length != 0)
            {
                _ = builder.Append('.');
            }

            _ = builder.Append(segment.Kind == ComponentPropertySegmentKind.FieldTag ? "tag:" : "index:");
            _ = builder.Append(segment.Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void ValidateMessage(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Length == 0)
        {
            throw new ArgumentException("A resource read diagnostic message cannot be empty.", nameof(message));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(message.Length, MaximumMessageLength, nameof(message));
    }

    /// <summary>Completes a successful read with the complete symbolic author value.</summary>
    /// <param name="author">The nonnull symbolic author value.</param>
    /// <param name="consumedBytes">The number of source bytes consumed.</param>
    /// <returns>The immutable successful result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="author"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired, used twice, or the consumed extent is incomplete.</exception>
    public WorldResourceReadResult<TAuthor> Complete([DisallowNull] in TAuthor author, nint consumedBytes)
    {
        EnsureCanIssueResult();

        if (author is null)
        {
            throw RecordFailure(new ArgumentNullException(nameof(author)));
        }

        if (consumedBytes != _sourceByteLength)
        {
            throw RecordFailure(new InvalidOperationException("A resource codec must consume the exact scoped record length."));
        }

        _resultIssued = true;
        return WorldResourceReadResult<TAuthor>.Read(author, consumedBytes, this);
    }

    /// <summary>Rejects a known resource-level read failure.</summary>
    /// <param name="kind">The defined resource read failure category.</param>
    /// <param name="sourceOffset">The in-range source byte offset.</param>
    /// <param name="path">The optional exact resource field path.</param>
    /// <param name="message">The nonempty bounded diagnostic message.</param>
    /// <returns>The immutable rejected result.</returns>
    /// <exception cref="ArgumentException"><paramref name="message"/> is empty or <paramref name="path"/> is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The failure kind or source offset is invalid.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired or a result was already issued.</exception>
    public WorldResourceReadResult<TAuthor> Reject(
        WorldResourceReadFailureKind kind,
        nint sourceOffset,
        ComponentFieldPath? path,
        string message)
    {
        EnsureCanIssueResult();

        if (kind is < WorldResourceReadFailureKind.MalformedRecord or > WorldResourceReadFailureKind.ConsumptionMismatch)
        {
            throw RecordFailure(new ArgumentOutOfRangeException(nameof(kind)));
        }

        if ((sourceOffset < 0) || (sourceOffset > _sourceByteLength))
        {
            throw RecordFailure(new ArgumentOutOfRangeException(nameof(sourceOffset)));
        }

        ValidateMessage(message);
        var context = ImmutableArray.CreateBuilder<DiagnosticContextEntry>(2);
        context.Add(new DiagnosticContextEntry("SourceOffset", sourceOffset.ToString(CultureInfo.InvariantCulture)));

        if (path is not null)
        {
            context.Add(new DiagnosticContextEntry("FieldPath", FormatPath(path)));
        }

        var diagnostic = CreateDiagnostic($"WorldResourceRead.{kind}", message, context.MoveToImmutable());
        _resultIssued = true;
        return WorldResourceReadResult<TAuthor>.Rejected([diagnostic], this);
    }

    /// <summary>Rejects a structural EBIN read failure while retaining its diagnostic facts.</summary>
    /// <param name="status">The non-complete EBIN status.</param>
    /// <param name="diagnostic">The optional structural diagnostic.</param>
    /// <returns>The immutable rejected result.</returns>
    /// <exception cref="ArgumentException">The structural diagnostic is invalid for the selected status.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="status"/> is Complete or undefined.</exception>
    /// <exception cref="InvalidOperationException">The binding is expired or a result was already issued.</exception>
    public WorldResourceReadResult<TAuthor> Reject(EbinReadStatus status, EbinReadDiagnostic? diagnostic)
    {
        EnsureCanIssueResult();

        if (status is not EbinReadStatus.NeedMoreData and not EbinReadStatus.InvalidRecord and not EbinReadStatus.UnsupportedSchema)
        {
            throw RecordFailure(new ArgumentOutOfRangeException(nameof(status)));
        }

        var context = ImmutableArray.CreateBuilder<DiagnosticContextEntry>(diagnostic.HasValue ? 5 : 0);
        var message = status.ToString();

        if (diagnostic is { } ebinDiagnostic)
        {
            if (string.IsNullOrEmpty(ebinDiagnostic.Category)
                || (ebinDiagnostic.Offset > (ulong)_sourceByteLength))
            {
                throw RecordFailure(new ArgumentException("The EBIN diagnostic does not match the scoped resource record.", nameof(diagnostic)));
            }

            message = ebinDiagnostic.Category;
            context.Add(new DiagnosticContextEntry("SourceOffset", ebinDiagnostic.Offset.ToString(CultureInfo.InvariantCulture)));
            context.Add(new DiagnosticContextEntry("SchemaId", ebinDiagnostic.SchemaId.ToString("D")));
            context.Add(new DiagnosticContextEntry("Tag", ebinDiagnostic.Tag.ToString(CultureInfo.InvariantCulture)));
            context.Add(new DiagnosticContextEntry("Claim", ebinDiagnostic.Claim.ToString(CultureInfo.InvariantCulture)));
            context.Add(new DiagnosticContextEntry("Limit", ebinDiagnostic.Limit.ToString(CultureInfo.InvariantCulture)));
        }

        ValidateMessage(message);
        var resultDiagnostic = CreateDiagnostic($"EbinRead.{status}", message, context.MoveToImmutable());
        _resultIssued = true;
        return WorldResourceReadResult<TAuthor>.Rejected([resultDiagnostic], this);
    }

    internal void Revoke() => _isActive = false;

    private void EnsureCanIssueResult()
    {
        if (!_isActive)
        {
            throw RecordFailure(new InvalidOperationException("The resource read binding has expired."));
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw RecordFailure(new InvalidOperationException("The resource read binding is restricted to its issuing thread."));
        }

        if (_resultIssued)
        {
            throw RecordFailure(new InvalidOperationException("A resource read binding can issue only one result."));
        }
    }

    private Exception RecordFailure(Exception exception)
    {
        _failure ??= exception;
        return _failure;
    }
}
