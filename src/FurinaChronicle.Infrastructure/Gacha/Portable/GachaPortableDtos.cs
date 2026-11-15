// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

internal sealed class GachaPortableManifestDto
{
    [JsonPropertyName("format")]
    public string? Format { get; init; }

    [JsonPropertyName("format_version")]
    public string? FormatVersion { get; init; }

    [JsonPropertyName("game")]
    public string? Game { get; init; }

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("source_archive")]
    public GachaPortableArchiveDto? SourceArchive { get; init; }

    [JsonPropertyName("scope")]
    public GachaPortableScopeDto? Scope { get; init; }

    [JsonPropertyName("accounts")]
    public List<GachaPortableAccountManifestDto?>? Accounts { get; init; }
}

internal sealed class GachaPortableArchiveDto
{
    [JsonPropertyName("archive_ref")]
    public Guid ArchiveReference { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

internal sealed class GachaPortableScopeDto
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("account_count")]
    public int AccountCount { get; init; }

    [JsonPropertyName("record_count")]
    public int RecordCount { get; init; }

    [JsonPropertyName("completeness_assertion")]
    public string? CompletenessAssertion { get; init; }
}

internal sealed class GachaPortableAccountManifestDto
{
    [JsonPropertyName("account_ref")]
    public Guid AccountReference { get; init; }

    [JsonPropertyName("role_identity_id")]
    public Guid RoleIdentityId { get; init; }

    [JsonPropertyName("natural_identity")]
    public GachaPortableNaturalIdentityDto? NaturalIdentity { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("uigf_path")]
    public string? UigfPath { get; init; }

    [JsonPropertyName("supplement_path")]
    public string? SupplementPath { get; init; }

    [JsonPropertyName("record_count")]
    public int RecordCount { get; init; }

    [JsonPropertyName("uigf_length")]
    public long UigfLength { get; init; }

    [JsonPropertyName("uigf_sha256")]
    public string? UigfSha256 { get; init; }

    [JsonPropertyName("supplement_length")]
    public long SupplementLength { get; init; }

    [JsonPropertyName("supplement_sha256")]
    public string? SupplementSha256 { get; init; }
}

internal sealed class GachaPortableNaturalIdentityDto
{
    [JsonPropertyName("game_biz")]
    public string? GameBiz { get; init; }

    [JsonPropertyName("server")]
    public string? Server { get; init; }

    [JsonPropertyName("uid")]
    public string? Uid { get; init; }
}

internal sealed class PortableUigfDocumentDto
{
    [JsonPropertyName("info")]
    public PortableUigfInfoDto? Info { get; init; }

    [JsonPropertyName("hk4e")]
    public List<PortableUigfAccountDto?>? Hk4e { get; init; }
}

internal sealed class PortableUigfInfoDto
{
    [JsonPropertyName("export_timestamp")]
    public long ExportTimestamp { get; init; }

    [JsonPropertyName("export_app")]
    public string? ExportApp { get; init; }

    [JsonPropertyName("export_app_version")]
    public string? ExportAppVersion { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

internal sealed class PortableUigfAccountDto
{
    [JsonPropertyName("uid")]
    public string? Uid { get; init; }

    [JsonPropertyName("timezone")]
    public int Timezone { get; init; }

    [JsonPropertyName("list")]
    public List<PortableUigfRecordDto?>? List { get; init; }
}

internal sealed class PortableUigfRecordDto
{
    [JsonPropertyName("uigf_gacha_type")]
    public string? UigfGachaType { get; init; }

    [JsonPropertyName("gacha_type")]
    public string? GachaType { get; init; }

    [JsonPropertyName("item_id")]
    public string? ItemId { get; init; }

    [JsonPropertyName("count")]
    public string? Count { get; init; }

    [JsonPropertyName("time")]
    public string? Time { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("item_type")]
    public string? ItemType { get; init; }

    [JsonPropertyName("rank_type")]
    public string? RankType { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

internal sealed class GachaPortableSupplementDto
{
    [JsonPropertyName("account_ref")]
    public Guid AccountReference { get; init; }

    [JsonPropertyName("external_record_id")]
    public string? ExternalRecordId { get; init; }

    [JsonPropertyName("occurred_at_utc_ticks")]
    public long OccurredAtUtcTicks { get; init; }

    [JsonPropertyName("occurred_at_offset_minutes")]
    public int OccurredAtOffsetMinutes { get; init; }

    [JsonPropertyName("origin")]
    public string? Origin { get; init; }

    [JsonPropertyName("fetched_at")]
    public DateTimeOffset? FetchedAt { get; init; }

    [JsonPropertyName("imported_at")]
    public DateTimeOffset? ImportedAt { get; init; }

    [JsonPropertyName("acquisition_batch_id")]
    public Guid? AcquisitionBatchId { get; init; }
}

internal sealed record PortablePayloadDescriptor(
    string Path,
    long Length,
    string Sha256);
