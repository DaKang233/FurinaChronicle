// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Services.Chronicle;

public static class ChronicleVersions
{
    public const int Projection = 1;
    public const int Profile = 1;
    public const int Cursor = 1;
    public const string SpiralAbyssCompletionRule =
        "genshin-spiral-abyss-completion-v1";
}

public sealed record ChronicleItemDescriptor(
    string ItemId,
    InventoryItemKind Kind,
    int? RankType);

public interface IChronicleItemCatalogSnapshot
{
    string Version { get; }

    ChronicleItemDescriptor? Find(string itemId);
}

public sealed class EmptyChronicleItemCatalogSnapshot
    : IChronicleItemCatalogSnapshot
{
    public static EmptyChronicleItemCatalogSnapshot Instance { get; } = new();

    private EmptyChronicleItemCatalogSnapshot()
    {
    }

    public string Version => "empty-v1";

    public ChronicleItemDescriptor? Find(string itemId) => null;
}

public sealed record ChronicleProfileRequest(
    ChronicleTimeline Timeline,
    ChronicleGranularity Granularity = ChronicleGranularity.Default,
    bool IncludeStateChanges = false);

public static class ChronicleProfileRules
{
    public static bool IsVisible(
        ChronicleEntry entry,
        ChronicleProfileRequest profile)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Timeline is ChronicleTimeline.None ||
            (profile.Timeline & (profile.Timeline - 1)) != 0)
        {
            throw new ArgumentException(
                "A Chronicle query selects exactly one timeline.",
                nameof(profile));
        }
        if (!entry.Timelines.HasFlag(profile.Timeline) ||
            profile.Granularity < entry.MinimumGranularity)
        {
            return false;
        }
        return entry.Kind != ChronicleEntryKind.StateChange ||
            profile.IncludeStateChanges;
    }
}

public enum ChronicleScopeKind
{
    Account = 1,
    Archive = 2,
}

public sealed record ChronicleScope(
    ChronicleScopeKind Kind,
    Guid Id,
    IReadOnlySet<Guid>? SelectedAccountIds = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || Id == Guid.Empty)
        {
            throw new ArgumentException("Chronicle scope is invalid.");
        }
        if (Kind == ChronicleScopeKind.Account &&
            SelectedAccountIds is { Count: > 0 })
        {
            throw new ArgumentException(
                "Account scopes cannot contain an account selection.");
        }
        if (SelectedAccountIds?.Any(id => id == Guid.Empty) == true)
        {
            throw new ArgumentException(
                "Selected account IDs cannot contain an empty value.");
        }
    }
}

public sealed record ChronicleFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool IncludeUndated = false,
    IReadOnlySet<ChronicleDomain>? Domains = null,
    IReadOnlySet<ChronicleEntryKind>? Kinds = null,
    IReadOnlySet<DataOrigin>? Origins = null,
    IReadOnlySet<int>? Ranks = null,
    IReadOnlySet<string>? ItemIds = null)
{
    public void Validate()
    {
        if (From is not null && To is not null && To <= From)
        {
            throw new ArgumentException(
                "Chronicle time filters use a non-empty half-open range.");
        }
        if (Domains?.Any(value => !Enum.IsDefined(value)) == true ||
            Kinds?.Any(value => !Enum.IsDefined(value)) == true ||
            Origins?.Any(value => !Enum.IsDefined(value)) == true ||
            Ranks?.Any(value => value is < 3 or > 5) == true)
        {
            throw new ArgumentException("Chronicle filter contains an invalid value.");
        }
    }
}

public enum ChronicleQueryError
{
    None = 0,
    InvalidCursor = 1,
    CursorInvalidated = 2,
    CursorExpired = 3,
    CursorVersionMismatch = 4,
    UnsupportedFilter = 5,
}

public sealed record ChroniclePage(
    IReadOnlyList<ChronicleEntry> Entries,
    string? NextCursor,
    string GenerationId,
    ChronicleQueryError Error = ChronicleQueryError.None,
    string? ErrorMessage = null);

public sealed record ChronicleQuery(
    ChronicleScope Scope,
    ChronicleProfileRequest Profile,
    ChronicleFilter Filter,
    int PageSize = 50,
    string? Cursor = null)
{
    public void Validate()
    {
        Scope.Validate();
        Filter.Validate();
        if (PageSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PageSize),
                "Chronicle page size must be between 1 and 500.");
        }
    }
}

public interface IChronicleQueryService
{
    Task<ChroniclePage> QueryAsync(
        ChronicleQuery query,
        CancellationToken cancellationToken = default);

    Task RebuildAsync(
        ChronicleScope scope,
        IChronicleItemCatalogSnapshot itemCatalog,
        CancellationToken cancellationToken = default);

    Task ClearCacheAsync(
        ChronicleScope scope,
        CancellationToken cancellationToken = default);

    Task ExportMarkdownAsync(
        ChronicleQuery query,
        TextWriter destination,
        CancellationToken cancellationToken = default);
}
