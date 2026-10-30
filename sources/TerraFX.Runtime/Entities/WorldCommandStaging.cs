// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldCommandStaging : IDisposable
{
    private readonly Dictionary<EntityToken, CommandRecord> _createsByToken = [];
    private readonly Lock _gate = new();
    private bool _isSealed;
    private readonly long _maximumBytes;
    private readonly int _maximumCommands;
    private readonly List<CommandPrefix> _prefixes = [];
    private readonly Dictionary<WriterKey, CommandPrefix> _prefixesByKey = [];
    private readonly List<CommandRecord> _records = [];
    private long _reservedBytes;
    private readonly World _world;

    internal WorldCommandStaging(World world, int maximumCommands, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCommands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _world = world;
        _maximumCommands = maximumCommands;
        _maximumBytes = maximumBytes;
    }

    internal long ReservedBytes
    {
        get
        {
            lock (_gate)
            {
                return _reservedBytes;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_reservedBytes != 0)
            {
                _world.ReleaseCommandStagingBytes(_reservedBytes);
                _reservedBytes = 0;
            }
        }
    }

    internal void AppendAdd<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        IComponentRegistration registration,
        in T value)
        where T : unmanaged
        => AppendValueCommand(
            dispatch,
            entity,
            tokenTarget: null,
            tokenParameterName: nameof(entity),
            registration,
            CommandKind.AddComponent,
            in value);

    internal void AppendAdd<T>(
        WorldSystemInvocationDispatch dispatch,
        EntityToken entity,
        IComponentRegistration registration,
        in T value)
        where T : unmanaged
        => AppendValueCommand(
            dispatch,
            default,
            entity,
            nameof(entity),
            registration,
            CommandKind.AddComponent,
            in value);

    internal EntityToken AppendCreate(WorldSystemInvocationDispatch dispatch, EntityDescription description)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(description);

        lock (_gate)
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("The World command writer is no longer accepting appends.");
            }

            if (_records.Count >= _maximumCommands)
            {
                throw new InvalidOperationException("The World deferred-command count limit was reached.");
            }

            var system = dispatch.Description;
            var writerPartition = dispatch.CurrentWriterPartition;
            var writerKey = new WriterKey(system.Stage, system.SystemId, system.Order, writerPartition);
            var newPrefix = !_prefixesByKey.TryGetValue(writerKey, out var prefix);
            prefix ??= new CommandPrefix(system, writerPartition);
            var sequence = prefix.NextSequence;
            var nextSequence = checked(sequence + 1);
            var bytes = checked(
                192L
                + ((long)description.Components.Length * 128L)
                + ((long)description.Tags.Length * Unsafe.SizeOf<Guid>()));

            if (!_world.TryReserveCommandStagingBytes(bytes, _reservedBytes, _maximumBytes))
            {
                throw new InvalidOperationException("The World command-staging byte budget was reached.");
            }

            try
            {
                _ = _records.EnsureCapacity(checked(_records.Count + 1));
                _ = _createsByToken.EnsureCapacity(checked(_createsByToken.Count + 1));

                if (newPrefix)
                {
                    _ = _prefixes.EnsureCapacity(checked(_prefixes.Count + 1));
                    _ = _prefixesByKey.EnsureCapacity(checked(_prefixesByKey.Count + 1));
                }

                var token = new EntityToken(prefix.PrefixId, sequence);
                var record = new CommandRecord(
                    prefix,
                    sequence,
                    default,
                    registration: null,
                    kind: CommandKind.CreateEntity,
                    initialization: null,
                    path: null,
                    target: null,
                    schemaId: Guid.Empty,
                    token: token,
                    description: description);

                if (newPrefix)
                {
                    _prefixesByKey.Add(writerKey, prefix);
                    _prefixes.Add(prefix);
                }

                _records.Add(record);
                _createsByToken.Add(token, record);
                prefix.NextSequence = nextSequence;
                _reservedBytes = checked(_reservedBytes + bytes);
                return token;
            }
            catch
            {
                _world.ReleaseCommandStagingBytes(bytes);
                throw;
            }
        }
    }

    internal void AppendDestroyEntity(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        HierarchyDestroyMode mode)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        if (mode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        lock (_gate)
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("The World command writer is no longer accepting appends.");
            }

            if (_records.Count >= _maximumCommands)
            {
                throw new InvalidOperationException("The World deferred-command count limit was reached.");
            }

            var description = dispatch.Description;
            var writerPartition = dispatch.CurrentWriterPartition;
            var writerKey = new WriterKey(
                description.Stage,
                description.SystemId,
                description.Order,
                writerPartition);
            var newPrefix = !_prefixesByKey.TryGetValue(writerKey, out var prefix);
            prefix ??= new CommandPrefix(description, writerPartition);
            var sequence = prefix.NextSequence;
            var nextSequence = checked(sequence + 1);
            const long bytes = 192;

            if (!_world.TryReserveCommandStagingBytes(bytes, _reservedBytes, _maximumBytes))
            {
                throw new InvalidOperationException("The World command-staging byte budget was reached.");
            }

            try
            {
                _ = _records.EnsureCapacity(checked(_records.Count + 1));

                if (newPrefix)
                {
                    _ = _prefixes.EnsureCapacity(checked(_prefixes.Count + 1));
                    _ = _prefixesByKey.EnsureCapacity(checked(_prefixesByKey.Count + 1));
                }

                var record = new CommandRecord(
                    prefix,
                    sequence,
                    entity,
                    registration: null,
                    kind: CommandKind.DestroyEntity,
                    initialization: null,
                    path: null,
                    target: null,
                    schemaId: Guid.Empty,
                    destroyMode: mode);

                if (newPrefix)
                {
                    _prefixesByKey.Add(writerKey, prefix);
                    _prefixes.Add(prefix);
                }

                _records.Add(record);
                prefix.NextSequence = nextSequence;
                _reservedBytes = checked(_reservedBytes + bytes);
            }
            catch
            {
                _world.ReleaseCommandStagingBytes(bytes);
                throw;
            }
        }
    }

    internal void AppendDestroyToken(
        WorldSystemInvocationDispatch dispatch,
        EntityToken token,
        HierarchyDestroyMode mode)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        if (mode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree)
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        lock (_gate)
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("The World command writer is no longer accepting appends.");
            }

            var description = dispatch.Description;
            var writerPartition = dispatch.CurrentWriterPartition;
            var writerKey = new WriterKey(
                description.Stage,
                description.SystemId,
                description.Order,
                writerPartition);
            var prefix = ValidateTokenLocked(writerKey, token, "entity");
            var create = _createsByToken[token];

            if (_records.Count >= _maximumCommands)
            {
                throw new InvalidOperationException("The World deferred-command count limit was reached.");
            }

            var sequence = prefix.NextSequence;
            var nextSequence = checked(sequence + 1);
            const long bytes = 192;

            if (!_world.TryReserveCommandStagingBytes(bytes, _reservedBytes, _maximumBytes))
            {
                throw new InvalidOperationException("The World command-staging byte budget was reached.");
            }

            try
            {
                _ = _records.EnsureCapacity(checked(_records.Count + 1));

                var record = new CommandRecord(
                    prefix,
                    sequence,
                    default,
                    registration: null,
                    kind: CommandKind.DestroyToken,
                    initialization: null,
                    path: null,
                    target: null,
                    schemaId: Guid.Empty,
                    entityTokenTarget: token,
                    destroyMode: mode);

                _records.Add(record);
                create.IsCanceled = true;
                prefix.NextSequence = nextSequence;
                _reservedBytes = checked(_reservedBytes + bytes);
            }
            catch
            {
                _world.ReleaseCommandStagingBytes(bytes);
                throw;
            }
        }
    }

    internal void AppendRemove(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        IComponentRegistration registration)
        => AppendWithoutValue(
            dispatch,
            entity,
            tokenTarget: null,
            tokenParameterName: nameof(entity),
            registration,
            CommandKind.RemoveComponent,
            Guid.Empty);

    internal void AppendRemove(
        WorldSystemInvocationDispatch dispatch,
        EntityToken entity,
        IComponentRegistration registration)
        => AppendWithoutValue(
            dispatch,
            default,
            entity,
            nameof(entity),
            registration,
            CommandKind.RemoveComponent,
            Guid.Empty);

    internal void AppendSet<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        IComponentRegistration registration,
        in T value)
        where T : unmanaged
        => AppendValueCommand(
            dispatch,
            entity,
            tokenTarget: null,
            tokenParameterName: nameof(entity),
            registration,
            CommandKind.Set,
            in value);

    internal void AppendSet<T>(
        WorldSystemInvocationDispatch dispatch,
        EntityToken entity,
        IComponentRegistration registration,
        in T value)
        where T : unmanaged
        => AppendValueCommand(
            dispatch,
            default,
            entity,
            nameof(entity),
            registration,
            CommandKind.Set,
            in value);

    internal void AppendSetEntityReference<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        IComponentRegistration registration,
        ComponentFieldPath path,
        Entity? target)
        where T : unmanaged
        => AppendEntityReferenceCommand<T>(dispatch, entity, null, registration, path, target, null);

    internal void AppendSetEntityReference<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        IComponentRegistration registration,
        ComponentFieldPath path,
        EntityToken? target)
        where T : unmanaged
        => AppendEntityReferenceCommand<T>(dispatch, entity, null, registration, path, null, target);

    internal void AppendSetEntityReference<T>(
        WorldSystemInvocationDispatch dispatch,
        EntityToken owner,
        IComponentRegistration registration,
        ComponentFieldPath path,
        Entity? target)
        where T : unmanaged
        => AppendEntityReferenceCommand<T>(dispatch, default, owner, registration, path, target, null);

    internal void AppendSetEntityReference<T>(
        WorldSystemInvocationDispatch dispatch,
        EntityToken owner,
        IComponentRegistration registration,
        ComponentFieldPath path,
        EntityToken? target)
        where T : unmanaged
        => AppendEntityReferenceCommand<T>(dispatch, default, owner, registration, path, null, target);

    internal void AppendSetEntityReferenceWithTokenArms<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity owner,
        EntityToken? ownerToken,
        IComponentRegistration registration,
        ComponentFieldPath path,
        Entity? target,
        EntityToken? targetToken)
        where T : unmanaged
        => AppendEntityReferenceCommand<T>(
            dispatch,
            owner,
            ownerToken,
            registration,
            path,
            target,
            targetToken);

    internal void AppendTag(WorldSystemInvocationDispatch dispatch, Entity entity, Guid schemaId, bool add)
        => AppendWithoutValue(
            dispatch,
            entity,
            null,
            nameof(entity),
            null,
            add ? CommandKind.AddTag : CommandKind.RemoveTag,
            schemaId);

    internal void AppendTag(WorldSystemInvocationDispatch dispatch, EntityToken entity, Guid schemaId, bool add)
        => AppendWithoutValue(
            dispatch,
            default,
            entity,
            nameof(entity),
            null,
            add ? CommandKind.AddTag : CommandKind.RemoveTag,
            schemaId);

    internal ImmutableArray<CommandReceipt> CreateReceipts(IReadOnlyDictionary<EntityToken, Entity> resolvedTokens)
    {
        ArgumentNullException.ThrowIfNull(resolvedTokens);

        if (!_isSealed)
        {
            throw new InvalidOperationException("Command staging must be sealed before receipts are prepared.");
        }

        var receipts = ImmutableArray.CreateBuilder<CommandReceipt>(_prefixes.Count);
        var recordIndex = 0;

        foreach (var prefix in _prefixes)
        {
            var prefixStart = recordIndex;

            while ((recordIndex < _records.Count) && ReferenceEquals(_records[recordIndex].Prefix, prefix))
            {
                recordIndex++;
            }

            var tokenCount = 0;
            var destroyedIdentityCount = 0;

            for (var index = prefixStart; index < recordIndex; index++)
            {
                var record = _records[index];
                tokenCount += (record.Kind == CommandKind.CreateEntity) ? 1 : 0;
                destroyedIdentityCount = checked(destroyedIdentityCount + record.DestroyedEntityIdentities.Length);
            }

            var tokens = ImmutableArray.CreateBuilder<EntityToken>(tokenCount);
            var resolvedEntities = ImmutableArray.CreateBuilder<Entity>(tokenCount);
            var canceled = ImmutableArray.CreateBuilder<bool>(tokenCount);
            var destroyedIdentities = ImmutableArray.CreateBuilder<WorldEntityIdentity>(destroyedIdentityCount);

            for (var index = prefixStart; index < recordIndex; index++)
            {
                var record = _records[index];
                destroyedIdentities.AddRange(record.DestroyedEntityIdentities);

                if (record.Kind != CommandKind.CreateEntity)
                {
                    continue;
                }

                if (record.IsCanceled)
                {
                    tokens.Add(record.Token);
                    resolvedEntities.Add(default);
                    canceled.Add(true);
                    continue;
                }

                if (!resolvedTokens.TryGetValue(record.Token, out var entity))
                {
                    throw new InvalidOperationException("A committed Create token has no resolved Entity.");
                }

                tokens.Add(record.Token);
                resolvedEntities.Add(entity);
                canceled.Add(false);
            }

            receipts.Add(new CommandReceipt(
                tokens.MoveToImmutable(),
                resolvedEntities.MoveToImmutable(),
                canceled.MoveToImmutable(),
                destroyedIdentities.MoveToImmutable()));
        }

        return receipts.MoveToImmutable();
    }

    internal bool IsTokenCanceled(EntityToken token)
    {
        lock (_gate)
        {
            if (!_createsByToken.TryGetValue(token, out var create))
            {
                throw new InvalidOperationException("A staged token command does not resolve to an earlier Create.");
            }

            return create.IsCanceled;
        }
    }

    internal ReadOnlySpan<CommandRecord> Seal()
    {
        lock (_gate)
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("World command staging was already sealed.");
            }

            _records.Sort(RecordComparer.Instance);
            _prefixes.Sort(PrefixComparer.Instance);
            _isSealed = true;
            return CollectionsMarshal.AsSpan(_records);
        }
    }

    internal void ValidateTokenTarget(
        WorldSystemInvocationDispatch dispatch,
        EntityToken token,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentException.ThrowIfNullOrEmpty(parameterName);

        lock (_gate)
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("The World command writer is no longer accepting appends.");
            }

            var description = dispatch.Description;
            var writerKey = new WriterKey(
                description.Stage,
                description.SystemId,
                description.Order,
                dispatch.CurrentWriterPartition);
            _ = ValidateTokenLocked(writerKey, token, parameterName);
        }
    }

    private void AppendEntityReferenceCommand<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity owner,
        EntityToken? ownerToken,
        IComponentRegistration registration,
        ComponentFieldPath path,
        Entity? target,
        EntityToken? targetToken)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(path);

        lock (_gate)
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("The World command writer is no longer accepting appends.");
            }

            var description = dispatch.Description;
            var writerPartition = dispatch.CurrentWriterPartition;
            var writerKey = new WriterKey(
                description.Stage,
                description.SystemId,
                description.Order,
                writerPartition);
            var newPrefix = false;
            CommandPrefix prefix;

            if (ownerToken is { } ownerTokenValue)
            {
                prefix = ValidateTokenLocked(writerKey, ownerTokenValue, "owner");
            }
            else if (targetToken is { } targetTokenValue)
            {
                prefix = ValidateTokenLocked(writerKey, targetTokenValue, "target");
            }
            else
            {
                newPrefix = !_prefixesByKey.TryGetValue(writerKey, out var prefixCandidate);
                prefix = prefixCandidate ?? new CommandPrefix(description, writerPartition);
            }

            if (targetToken is { } validatedTargetToken)
            {
                _ = ValidateTokenLocked(writerKey, validatedTargetToken, "target");
            }

            if (_records.Count >= _maximumCommands)
            {
                throw new InvalidOperationException("The World deferred-command count limit was reached.");
            }

            var sequence = prefix.NextSequence;
            var nextSequence = checked(sequence + 1);
            var bytes = checked(192L + Unsafe.SizeOf<T>());

            if (!_world.TryReserveCommandStagingBytes(bytes, _reservedBytes, _maximumBytes))
            {
                throw new InvalidOperationException("The World command-staging byte budget was reached.");
            }

            try
            {
                _ = _records.EnsureCapacity(checked(_records.Count + 1));

                if (newPrefix)
                {
                    _ = _prefixes.EnsureCapacity(checked(_prefixes.Count + 1));
                    _ = _prefixesByKey.EnsureCapacity(checked(_prefixesByKey.Count + 1));
                }

                var record = new CommandRecord(
                    prefix,
                    sequence,
                    owner,
                    registration,
                    CommandKind.SetEntityReference,
                    initialization: null,
                    path: path,
                    target: target,
                    schemaId: Guid.Empty,
                    ownerToken: ownerToken,
                    referenceTargetToken: targetToken);

                if (newPrefix)
                {
                    _prefixesByKey.Add(writerKey, prefix);
                    _prefixes.Add(prefix);
                }

                _records.Add(record);
                prefix.NextSequence = nextSequence;
                _reservedBytes = checked(_reservedBytes + bytes);
            }
            catch
            {
                _world.ReleaseCommandStagingBytes(bytes);
                throw;
            }
        }
    }

    private void AppendValueCommand<T>(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        EntityToken? tokenTarget,
        string tokenParameterName,
        IComponentRegistration registration,
        CommandKind kind,
        in T value)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(registration);

        lock (_gate)
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("The World command writer is no longer accepting appends.");
            }

            if (_records.Count >= _maximumCommands)
            {
                throw new InvalidOperationException("The World deferred-command count limit was reached.");
            }

            var description = dispatch.Description;
            var writerPartition = dispatch.CurrentWriterPartition;
            var writerKey = new WriterKey(
                description.Stage,
                description.SystemId,
                description.Order,
                writerPartition);
            var newPrefix = false;
            CommandPrefix prefix;

            if (tokenTarget is { } token)
            {
                prefix = ValidateTokenLocked(writerKey, token, tokenParameterName);
            }
            else
            {
                newPrefix = !_prefixesByKey.TryGetValue(writerKey, out var prefixCandidate);
                prefix = prefixCandidate ?? new CommandPrefix(description, writerPartition);
            }

            if (_records.Count >= _maximumCommands)
            {
                throw new InvalidOperationException("The World deferred-command count limit was reached.");
            }

            var sequence = prefix.NextSequence;
            var nextSequence = checked(sequence + 1);
            var bytes = checked(192L + Unsafe.SizeOf<T>());

            if (!_world.TryReserveCommandStagingBytes(bytes, _reservedBytes, _maximumBytes))
            {
                throw new InvalidOperationException("The World command-staging byte budget was reached.");
            }

            try
            {
                _ = _records.EnsureCapacity(checked(_records.Count + 1));

                if (newPrefix)
                {
                    _ = _prefixes.EnsureCapacity(checked(_prefixes.Count + 1));
                    _ = _prefixesByKey.EnsureCapacity(checked(_prefixesByKey.Count + 1));
                }

                var initialization = ComponentInitialization.Value(in value);
                var record = new CommandRecord(
                    prefix,
                    sequence,
                    entity,
                    registration,
                    kind,
                    initialization,
                    path: null,
                    target: null,
                    schemaId: Guid.Empty,
                    entityTokenTarget: tokenTarget);

                if (newPrefix)
                {
                    _prefixesByKey.Add(writerKey, prefix);
                    _prefixes.Add(prefix);
                }

                _records.Add(record);
                prefix.NextSequence = nextSequence;
                _reservedBytes = checked(_reservedBytes + bytes);
            }
            catch
            {
                _world.ReleaseCommandStagingBytes(bytes);
                throw;
            }
        }
    }

    private void AppendWithoutValue(
        WorldSystemInvocationDispatch dispatch,
        Entity entity,
        EntityToken? tokenTarget,
        string tokenParameterName,
        IComponentRegistration? registration,
        CommandKind kind,
        Guid schemaId)
    {
        ArgumentNullException.ThrowIfNull(dispatch);

        lock (_gate)
        {
            if (_isSealed)
            {
                throw new InvalidOperationException("The World command writer is no longer accepting appends.");
            }

            var description = dispatch.Description;
            var writerPartition = dispatch.CurrentWriterPartition;
            var writerKey = new WriterKey(
                description.Stage,
                description.SystemId,
                description.Order,
                writerPartition);
            var newPrefix = false;
            CommandPrefix prefix;

            if (tokenTarget is { } token)
            {
                prefix = ValidateTokenLocked(writerKey, token, tokenParameterName);
            }
            else
            {
                newPrefix = !_prefixesByKey.TryGetValue(writerKey, out var prefixCandidate);
                prefix = prefixCandidate ?? new CommandPrefix(description, writerPartition);
            }

            if (_records.Count >= _maximumCommands)
            {
                throw new InvalidOperationException("The World deferred-command count limit was reached.");
            }

            var sequence = prefix.NextSequence;
            var nextSequence = checked(sequence + 1);
            var bytes = 192L;

            if (!_world.TryReserveCommandStagingBytes(bytes, _reservedBytes, _maximumBytes))
            {
                throw new InvalidOperationException("The World command-staging byte budget was reached.");
            }

            try
            {
                _ = _records.EnsureCapacity(checked(_records.Count + 1));

                if (newPrefix)
                {
                    _ = _prefixes.EnsureCapacity(checked(_prefixes.Count + 1));
                    _ = _prefixesByKey.EnsureCapacity(checked(_prefixesByKey.Count + 1));
                }

                var record = new CommandRecord(
                    prefix,
                    sequence,
                    entity,
                    registration,
                    kind,
                    initialization: null,
                    path: null,
                    target: null,
                    schemaId: schemaId,
                    entityTokenTarget: tokenTarget);

                if (newPrefix)
                {
                    _prefixesByKey.Add(writerKey, prefix);
                    _prefixes.Add(prefix);
                }

                _records.Add(record);
                prefix.NextSequence = nextSequence;
                _reservedBytes = checked(_reservedBytes + bytes);
            }
            catch
            {
                _world.ReleaseCommandStagingBytes(bytes);
                throw;
            }
        }
    }

    private CommandPrefix ValidateTokenLocked(WriterKey writerKey, EntityToken token, string parameterName)
    {
        if (!token.IsValid
            || !_prefixesByKey.TryGetValue(writerKey, out var prefix)
            || (prefix.PrefixId != token.WriterPrefixId)
            || (token.LocalSequence >= prefix.NextSequence)
            || !_createsByToken.TryGetValue(token, out var create)
            || create.IsCanceled)
        {
            throw new ArgumentException("The Entity token is not an earlier live Create in this writer prefix.", parameterName);
        }

        return prefix;
    }

    internal enum CommandKind
    {
        AddComponent,
        AddTag,
        CreateEntity,
        DestroyEntity,
        DestroyToken,
        RemoveComponent,
        RemoveTag,
        Set,
        SetEntityReference,
    }

    internal sealed class CommandPrefix
    {
        internal CommandPrefix(SystemDescription description, int writerPartition)
        {
            Description = description;
            WriterPartition = writerPartition;
            PrefixId = Guid.NewGuid();
        }

        internal SystemDescription Description { get; }

        internal long NextSequence { get; set; }

        internal Guid PrefixId { get; }

        internal int WriterPartition { get; }
    }

    internal sealed class CommandRecord
    {
        internal CommandRecord(
            CommandPrefix prefix,
            long sequence,
            Entity entity,
            IComponentRegistration? registration,
            CommandKind kind,
            ComponentInitialization? initialization,
            ComponentFieldPath? path,
            Entity? target,
            Guid schemaId,
            EntityToken token = default,
            EntityDescription? description = null,
            EntityToken? entityTokenTarget = null,
            EntityToken? ownerToken = null,
            EntityToken? referenceTargetToken = null,
            HierarchyDestroyMode? destroyMode = null)
        {
            ArgumentNullException.ThrowIfNull(prefix);

            switch (kind)
            {
                case CommandKind.CreateEntity:
                {
                    ArgumentNullException.ThrowIfNull(description);

                    if ((registration is not null)
                        || (initialization is not null)
                        || (path is not null)
                        || (target is not null)
                        || (schemaId != Guid.Empty)
                        || (entityTokenTarget is not null)
                        || (ownerToken is not null)
                        || (referenceTargetToken is not null)
                        || (destroyMode is not null)
                        || !token.IsValid
                        || (token.WriterPrefixId != prefix.PrefixId)
                        || (token.LocalSequence != sequence))
                    {
                        throw new ArgumentException("A Create command has inconsistent token or payload data.");
                    }

                    break;
                }

                case CommandKind.AddComponent:
                case CommandKind.Set:
                {
                    ArgumentNullException.ThrowIfNull(registration);
                    ArgumentNullException.ThrowIfNull(initialization);

                    if ((path is not null) || (target is not null) || (schemaId != Guid.Empty))
                    {
                        throw new ArgumentException("A value command cannot contain Entity-reference or tag data.");
                    }

                    break;
                }

                case CommandKind.RemoveComponent:
                {
                    ArgumentNullException.ThrowIfNull(registration);

                    if ((initialization is not null)
                        || (path is not null)
                        || (target is not null)
                        || (schemaId != Guid.Empty))
                    {
                        throw new ArgumentException("A component removal command cannot contain payload data.");
                    }

                    break;
                }

                case CommandKind.SetEntityReference:
                {
                    ArgumentNullException.ThrowIfNull(registration);
                    ArgumentNullException.ThrowIfNull(path);

                    if ((initialization is not null)
                        || (schemaId != Guid.Empty)
                        || (entityTokenTarget is not null)
                        || (ownerToken is { IsValid: false })
                        || (referenceTargetToken is { IsValid: false })
                        || ((ownerToken is not null) && entity.IsValid)
                        || ((referenceTargetToken is not null) && (target is not null)))
                    {
                        throw new ArgumentException("An Entity-reference command has inconsistent payload data.");
                    }

                    break;
                }

                case CommandKind.AddTag:
                case CommandKind.RemoveTag:
                {
                    if ((registration is not null)
                        || (initialization is not null)
                        || (path is not null)
                        || (target is not null)
                        || (ownerToken is not null)
                        || (referenceTargetToken is not null))
                    {
                        throw new ArgumentException("A tag command cannot contain component data.");
                    }

                    ArgumentOutOfRangeException.ThrowIfEqual(schemaId, Guid.Empty);
                    break;
                }

                case CommandKind.DestroyToken:
                {
                    if (entity.IsValid
                        || (registration is not null)
                        || (initialization is not null)
                        || (path is not null)
                        || (target is not null)
                        || (schemaId != Guid.Empty)
                        || token.IsValid
                        || (description is not null)
                        || (entityTokenTarget is not { IsValid: true })
                        || (ownerToken is not null)
                        || (referenceTargetToken is not null)
                        || (destroyMode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree))
                    {
                        throw new ArgumentException("A token-destruction command has inconsistent payload data.");
                    }

                    break;
                }

                case CommandKind.DestroyEntity:
                {
                    if (!entity.IsValid
                        || (registration is not null)
                        || (initialization is not null)
                        || (path is not null)
                        || (target is not null)
                        || (schemaId != Guid.Empty)
                        || token.IsValid
                        || (description is not null)
                        || (entityTokenTarget is not null)
                        || (ownerToken is not null)
                        || (referenceTargetToken is not null)
                        || (destroyMode is not HierarchyDestroyMode.DetachChildrenKeepWorld and not HierarchyDestroyMode.DestroySubtree))
                    {
                        throw new ArgumentException("A live-Entity destruction command has inconsistent payload data.");
                    }

                    break;
                }

                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }

            if ((kind != CommandKind.CreateEntity) && (token.IsValid || (description is not null)))
            {
                throw new ArgumentException("Only a Create command can carry a temporary token or EntityDescription.");
            }

            if ((kind != CommandKind.SetEntityReference)
                && ((ownerToken is not null) || (referenceTargetToken is not null)))
            {
                throw new ArgumentException("Only an Entity-reference command can contain reference tokens.");
            }

            if (entityTokenTarget is { IsValid: false })
            {
                throw new ArgumentException("A token-target command requires an issued Entity token.", nameof(entityTokenTarget));
            }

            if (entityTokenTarget is not null
                && kind is not (CommandKind.DestroyToken
                    or CommandKind.AddComponent
                    or CommandKind.AddTag
                    or CommandKind.RemoveComponent
                    or CommandKind.RemoveTag
                    or CommandKind.Set))
            {
                throw new ArgumentException("This command kind does not accept a temporary Entity target.", nameof(entityTokenTarget));
            }

            if ((kind is not (CommandKind.DestroyEntity or CommandKind.DestroyToken)) && (destroyMode is not null))
            {
                throw new ArgumentException("Only a destruction command can specify a hierarchy mode.", nameof(destroyMode));
            }

            Prefix = prefix;
            Sequence = sequence;
            Entity = entity;
            Kind = kind;
            DestroyMode = destroyMode;
            Path = path;
            ReferenceTarget = target;
            Registration = registration;
            Initialization = initialization;
            SchemaId = schemaId;
            Token = token;
            Description = description;
            EntityTokenTarget = entityTokenTarget;
            OwnerToken = ownerToken;
            ReferenceTargetToken = referenceTargetToken;
        }

        internal EntityDescription? Description { get; }

        internal HierarchyDestroyMode? DestroyMode { get; }

        internal ImmutableArray<WorldEntityIdentity> DestroyedEntityIdentities { get; set; } = [];

        internal Entity Entity { get; }

        internal EntityToken? EntityTokenTarget { get; }

        internal ComponentInitialization? Initialization { get; }

        internal bool IsCanceled { get; set; }

        internal CommandKind Kind { get; }

        internal EntityToken? OwnerToken { get; }

        internal ComponentFieldPath? Path { get; }

        internal CommandPrefix Prefix { get; }

        internal Entity? ReferenceTarget { get; }

        internal EntityToken? ReferenceTargetToken { get; }

        internal IComponentRegistration? Registration { get; }

        internal Guid SchemaId { get; }

        internal long Sequence { get; }

        internal EntityToken Token { get; }
    }

    internal readonly record struct WriterKey(
        SimulationStage Stage,
        Guid SystemId,
        int SystemOrder,
        int WriterPartition);

    private sealed class PrefixComparer : IComparer<CommandPrefix>
    {
        internal static PrefixComparer Instance { get; } = new();

        public int Compare(CommandPrefix? left, CommandPrefix? right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            var comparison = ((int)left.Description.Stage).CompareTo((int)right.Description.Stage);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Description.Order.CompareTo(right.Description.Order);

            if (comparison != 0)
            {
                return comparison;
            }

            return left.WriterPartition.CompareTo(right.WriterPartition);
        }
    }

    private sealed class RecordComparer : IComparer<CommandRecord>
    {
        internal static RecordComparer Instance { get; } = new();

        public int Compare(CommandRecord? left, CommandRecord? right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            var comparison = ((int)left.Prefix.Description.Stage).CompareTo((int)right.Prefix.Description.Stage);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Prefix.Description.Order.CompareTo(right.Prefix.Description.Order);

            if (comparison != 0)
            {
                return comparison;
            }

            comparison = left.Prefix.WriterPartition.CompareTo(right.Prefix.WriterPartition);
            return (comparison != 0) ? comparison : left.Sequence.CompareTo(right.Sequence);
        }
    }
}
