// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace TerraFX.Entities;

/// <summary>Defines a system's identity, execution stage, dependencies, and resource access.</summary>
public sealed class SystemDescription
{
    private const int MaximumAccessCount = 256;
    private const int MaximumDependencyCount = 4095;

    /// <summary>Initializes an immutable system description.</summary>
    /// <param name="systemId">The nonempty system identity.</param>
    /// <param name="order">The unique signed World registration order.</param>
    /// <param name="stage">The user execution stage, excluding Commit.</param>
    /// <param name="access">The declared resource accesses.</param>
    /// <param name="before">System identities that follow this system.</param>
    /// <param name="after">System identities that precede this system.</param>
    /// <param name="threadMode">The execution eligibility.</param>
    /// <exception cref="ArgumentException">An identity or array entry is invalid, duplicated, or inconsistent.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count, stage, or thread mode is outside its supported domain.</exception>
    public SystemDescription(
        Guid systemId,
        int order,
        SimulationStage stage,
        ImmutableArray<SystemResourceAccess> access,
        ImmutableArray<Guid> before,
        ImmutableArray<Guid> after,
        SystemThreadMode threadMode)
    {
        if (systemId == Guid.Empty)
        {
            throw new ArgumentException("A system identity cannot be empty.", nameof(systemId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative((int)stage, nameof(stage));
        ArgumentOutOfRangeException.ThrowIfGreaterThan((int)stage, (int)SimulationStage.Extract, nameof(stage));

        if (threadMode is not (SystemThreadMode.OwnerThread or SystemThreadMode.Parallel))
        {
            throw new ArgumentOutOfRangeException(nameof(threadMode));
        }

        ValidateAccess(access);
        ValidateDependencies(systemId, before, nameof(before));
        ValidateDependencies(systemId, after, nameof(after));

        SystemId = systemId;
        Order = order;
        Stage = stage;
        Access = ImmutableArrayCopy.Create(access);
        Before = ImmutableArrayCopy.Create(before);
        After = ImmutableArrayCopy.Create(after);
        ThreadMode = threadMode;
    }

    /// <summary>Gets the immutable resource access declarations.</summary>
    public ImmutableArray<SystemResourceAccess> Access { get; }

    /// <summary>Gets the systems that must execute before this system.</summary>
    public ImmutableArray<Guid> After { get; }

    /// <summary>Gets the systems that must execute after this system.</summary>
    public ImmutableArray<Guid> Before { get; }

    /// <summary>Gets the unique signed execution and command-order key.</summary>
    public int Order { get; }

    /// <summary>Gets the user execution stage.</summary>
    public SimulationStage Stage { get; }

    /// <summary>Gets the stable system identity.</summary>
    public Guid SystemId { get; }

    /// <summary>Gets the system's execution eligibility.</summary>
    public SystemThreadMode ThreadMode { get; }

    private static void ValidateAccess(ImmutableArray<SystemResourceAccess> access)
    {
        if (access.IsDefault)
        {
            throw new ArgumentException("The access array cannot be default.", nameof(access));
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(access.Length, MaximumAccessCount, nameof(access));

        var identities = new HashSet<(SystemResourceKind Kind, Guid ResourceId)>();

        foreach (var item in access)
        {
            if (!item.IsValid)
            {
                throw new ArgumentException("Every access declaration must be intrinsically valid.", nameof(access));
            }

            if (!identities.Add((item.Kind, item.ResourceId)))
            {
                throw new ArgumentException("Access declarations cannot repeat a resource identity.", nameof(access));
            }
        }
    }

    private static void ValidateDependencies(Guid systemId, ImmutableArray<Guid> dependencies, string parameterName)
    {
        if (dependencies.IsDefault)
        {
            throw new ArgumentException("A dependency array cannot be default.", parameterName);
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(dependencies.Length, MaximumDependencyCount, parameterName);

        var identities = new HashSet<Guid>();

        foreach (var dependency in dependencies)
        {
            if (dependency == Guid.Empty)
            {
                throw new ArgumentException("A dependency identity cannot be empty.", parameterName);
            }

            if (dependency == systemId)
            {
                throw new ArgumentException("A system cannot depend on itself.", parameterName);
            }

            if (!identities.Add(dependency))
            {
                throw new ArgumentException("A dependency array cannot contain duplicates.", parameterName);
            }
        }
    }
}
