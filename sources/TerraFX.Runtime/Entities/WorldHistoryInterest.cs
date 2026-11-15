// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See LICENSE.md in the repository root for more information.

using System;
using System.Threading;

namespace TerraFX.Entities;

internal sealed class WorldHistoryInterest : IDisposable
{
    private readonly WorldAdmissionDomain _domain;
    private uint _historyRootGeneration;
    private int _historyRootIndex = -1;
    private readonly Guid _identityBranchId;
    private uint _interestGeneration;
    private int _interestIndex = -1;
    private int _isDisposed;
    private readonly Guid _issuanceNamespace;
    private readonly Guid _lineageId;
    private readonly Guid _worldGuid;

    internal WorldHistoryInterest(
        WorldAdmissionDomain domain,
        Guid worldGuid,
        Guid lineageId,
        Guid identityBranchId,
        Guid issuanceNamespace)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentOutOfRangeException.ThrowIfEqual(worldGuid, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(lineageId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(identityBranchId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(issuanceNamespace, Guid.Empty);
        _domain = domain;
        _identityBranchId = identityBranchId;
        _issuanceNamespace = issuanceNamespace;
        _lineageId = lineageId;
        _worldGuid = worldGuid;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _domain.ReleaseHistoryInterest(
                _historyRootIndex,
                _historyRootGeneration,
                _worldGuid,
                _lineageId,
                _identityBranchId,
                _issuanceNamespace,
                _interestIndex,
                _interestGeneration);
        }
    }

    internal void Issue(
        int historyRootIndex,
        uint historyRootGeneration,
        int interestIndex,
        uint interestGeneration)
    {
        if ((historyRootIndex < 0)
            || (historyRootGeneration == 0)
            || (interestIndex < 0)
            || (interestGeneration == 0)
            || (Volatile.Read(ref _isDisposed) != 0))
        {
            throw new InvalidOperationException("The World history interest cannot receive this domain slot.");
        }

        _historyRootIndex = historyRootIndex;
        _historyRootGeneration = historyRootGeneration;
        _interestIndex = interestIndex;
        _interestGeneration = interestGeneration;
    }
}
