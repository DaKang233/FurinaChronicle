// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Services.Archives;

public sealed record UnresolvedGameAccountDiagnostic(
    Guid GameAccountId,
    Guid PlayerArchiveId,
    string PlayerArchiveName,
    string Uid,
    GameServerRegion ServerRegion,
    UnresolvedGameAccountReason Reason);
