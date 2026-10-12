// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;

namespace TerraFX.Entities;

internal sealed class SystemScheduleException : InvalidOperationException
{
    private readonly Guid[]? _cyclePath;
    private readonly int _cyclePathLength;
    private readonly SystemScheduleFailure _failure;
    private readonly long _maximum;
    private readonly Guid _relatedSystemId;
    private readonly long _required;
    private readonly Guid _resourceId;
    private readonly int _stage;
    private readonly Guid _systemId;

    public SystemScheduleException()
        : base(GetMessage(SystemScheduleFailure.Unknown))
    {
        _cyclePath = null;
        _cyclePathLength = 0;
        _failure = SystemScheduleFailure.Unknown;
        _maximum = 0;
        _required = 0;
        _resourceId = Guid.Empty;
        _stage = -1;
        _systemId = Guid.Empty;
        _relatedSystemId = Guid.Empty;
    }

    public SystemScheduleException(string? message)
        : base(message ?? GetMessage(SystemScheduleFailure.Unknown))
    {
        _cyclePath = null;
        _cyclePathLength = 0;
        _failure = SystemScheduleFailure.Unknown;
        _maximum = 0;
        _required = 0;
        _resourceId = Guid.Empty;
        _stage = -1;
        _systemId = Guid.Empty;
        _relatedSystemId = Guid.Empty;
    }

    public SystemScheduleException(string? message, Exception? innerException)
        : base(message ?? GetMessage(SystemScheduleFailure.Unknown), innerException)
    {
        _cyclePath = null;
        _cyclePathLength = 0;
        _failure = SystemScheduleFailure.Unknown;
        _maximum = 0;
        _required = 0;
        _resourceId = Guid.Empty;
        _stage = -1;
        _systemId = Guid.Empty;
        _relatedSystemId = Guid.Empty;
    }

    internal SystemScheduleException(
        SystemScheduleFailure failure,
        Guid systemId,
        Guid relatedSystemId,
        Guid resourceId)
        : base(GetMessage(failure))
    {
        _cyclePath = null;
        _cyclePathLength = 0;
        _failure = failure;
        _maximum = 0;
        _required = 0;
        _stage = -1;
        _systemId = systemId;
        _relatedSystemId = relatedSystemId;
        _resourceId = resourceId;
    }

    internal SystemScheduleException(SystemScheduleFailure failure, Guid systemId, Guid relatedSystemId = default)
        : base(GetMessage(failure))
    {
        _cyclePath = null;
        _cyclePathLength = 0;
        _failure = failure;
        _maximum = 0;
        _required = 0;
        _resourceId = Guid.Empty;
        _stage = -1;
        _systemId = systemId;
        _relatedSystemId = relatedSystemId;
    }

    internal SystemScheduleException(SystemScheduleFailure failure, int stage, Guid[] cyclePath, int cyclePathLength)
        : base(GetMessage(failure))
    {
        _cyclePath = cyclePath;
        _cyclePathLength = cyclePathLength;
        _failure = failure;
        _maximum = 0;
        _required = 0;
        _stage = stage;
        _systemId = Guid.Empty;
        _relatedSystemId = Guid.Empty;
        _resourceId = Guid.Empty;
    }

    internal SystemScheduleException(SystemScheduleFailure failure, long required, long maximum)
        : base(GetMessage(failure))
    {
        _cyclePath = null;
        _cyclePathLength = 0;
        _failure = failure;
        _maximum = maximum;
        _relatedSystemId = Guid.Empty;
        _required = required;
        _stage = -1;
        _systemId = Guid.Empty;
        _resourceId = Guid.Empty;
    }

    internal ReadOnlySpan<Guid> CyclePath => (_cyclePath is null) ? [] : _cyclePath.AsSpan(0, _cyclePathLength);

    internal SystemScheduleFailure Failure => _failure;

    internal long Maximum => _maximum;

    internal Guid RelatedSystemId => _relatedSystemId;

    internal long Required => _required;

    internal Guid ResourceId => _resourceId;

    internal int Stage => _stage;

    internal Guid SystemId => _systemId;

    private static string GetMessage(SystemScheduleFailure failure)
        => failure switch {
            SystemScheduleFailure.Unknown => "System scheduling failed.",
            SystemScheduleFailure.SystemCountExceeded => "The system registration exceeds its count limit.",
            SystemScheduleFailure.AccessCountExceeded => "The system registration exceeds its access limit.",
            SystemScheduleFailure.DependencyCountExceeded => "The system registration exceeds its dependency limit.",
            SystemScheduleFailure.GraphBudgetExceeded => "The compiled system graph exceeds its byte budget.",
            SystemScheduleFailure.DuplicateSystemId => "The system registration contains a duplicate identity.",
            SystemScheduleFailure.DuplicateSystemOrder => "The system registration contains a duplicate order.",
            SystemScheduleFailure.UnknownDependency => "A system dependency does not resolve in the registration.",
            SystemScheduleFailure.BackwardStageDependency => "A system dependency points backward across a fixed stage.",
            SystemScheduleFailure.ConflictBudgetExceeded => "The system registration exceeds its conflict-comparison budget.",
            SystemScheduleFailure.DependencyCycle => "The system dependency graph contains a cycle.",
            SystemScheduleFailure.ResourceCountExceeded => "The resource registration exceeds its count limit.",
            SystemScheduleFailure.DuplicateResourceId => "The resource registration contains a duplicate identity.",
            SystemScheduleFailure.UnknownResource => "A system resource access does not resolve in the registration.",
            SystemScheduleFailure.ResourcePartitionRangeExceeded => "A system resource range exceeds the registered partition domain.",
            SystemScheduleFailure.ParallelResourceAccessDenied => "A parallel system accesses a resource that is not parallel-enabled.",
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
}
