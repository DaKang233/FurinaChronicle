// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;

namespace FurinaChronicle.Services.Gacha.Abstractions;

public interface IGachaEventCatalog
{
    ValueTask<IReadOnlyList<GachaEventPeriod>> GetAllAsync(
        GachaGame game,
        CancellationToken cancellationToken = default);
}
