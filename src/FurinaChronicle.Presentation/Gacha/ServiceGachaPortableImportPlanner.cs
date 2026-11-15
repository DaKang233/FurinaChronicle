// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Presentation.Gacha;

// Adapts the production planning use case to the coordinator seam. The adapter adds no policy:
// conflicts, identity mapping and archive matching all remain in PlanGachaPortableImport.
public sealed class ServiceGachaPortableImportPlanner(PlanGachaPortableImport planner)
    : IGachaPortableImportPlanner
{
    public Task<GachaPortableImportPlan> ExecuteAsync(
        GachaPortableIndexedImportPlanRequest request,
        CancellationToken cancellationToken = default) =>
        planner.ExecuteAsync(request, cancellationToken);
}
