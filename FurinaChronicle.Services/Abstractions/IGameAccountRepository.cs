// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Abstractions
{
    public interface IGameAccountRepository
    {
        Task<IReadOnlyList<GameAccount>> GetByArchiveIdAsync(Guid archiveId, CancellationToken cancellationToken = default);
        Task<GameAccount?> GetByIdAsync(Guid gameAcountId, CancellationToken cancellationToken = default);
        Task<GameAccount?> GetByArchiveIdAndNaturalIdentityAsync(
            Guid archiveId,
            GameRoleNaturalIdentity naturalIdentity,
            CancellationToken cancellationToken = default);
        Task<GameRoleIdentity?> GetRoleIdentityByNaturalIdentityAsync(
            GameRoleNaturalIdentity naturalIdentity,
            CancellationToken cancellationToken = default);
        Task<GameRoleIdentity?> GetRoleIdentityByIdAsync(
            GameRoleIdentityId roleIdentityId,
            CancellationToken cancellationToken = default);
        Task<IReadOnlyList<GameAccount>> GetUnresolvedAsync(
            CancellationToken cancellationToken = default);
        Task AddAsync(GameAccount account,  CancellationToken cancellationToken = default);
        Task UpdateAsync(GameAccount account, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid gameAccountId,  CancellationToken cancellationToken = default);
    }
}
