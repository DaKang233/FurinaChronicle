// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;

namespace FurinaChronicle.Services.HistoryDomains.Importing;

/// <summary>
/// Reads bounded collector snapshots into domain facts. The source format is
/// an internal adapter boundary, not a public Portable or backup format.
/// </summary>
public interface IArchiveDomainSourceFileReader
{
    Task<IReadOnlyList<ItemStateObservation>> ReadItemObservationsAsync(
        Stream source,
        Guid targetGameAccountId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChallengeObservation>> ReadChallengeObservationsAsync(
        Stream source,
        Guid targetGameAccountId,
        CancellationToken cancellationToken = default);
}
