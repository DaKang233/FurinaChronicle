// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha.Portable;

public sealed record GachaPortablePackage(
    DateTimeOffset GeneratedAt,
    GachaPortableArchive SourceArchive,
    IReadOnlyList<GachaPortableAccount> Accounts)
{
    public int RecordCount => Accounts.Sum(account => account.Records.Count);
}

public sealed record GachaPortableArchive(
    Guid ArchiveReference,
    string Name);

public sealed record GachaPortableAccount(
    Guid AccountReference,
    GameRoleIdentity RoleIdentity,
    string? DisplayName,
    IReadOnlyList<GachaRecord> Records);

public sealed record GachaPortableExportAccount(
    Guid AccountReference,
    GameRoleIdentity RoleIdentity,
    string? DisplayName,
    int RecordCount);

public sealed record GachaPortableWriteResult(
    int AccountCount,
    int RecordCount,
    long UncompressedPayloadBytes);

public sealed record GachaPortableReadResult(
    string FormatVersion,
    GachaPortablePackage Package);

public sealed record GachaPortableIndexedAccount(
    Guid AccountReference,
    GameRoleIdentity RoleIdentity,
    string? DisplayName,
    int RecordCount);

public enum GachaPortableIndexedPlanKind
{
    Add = 1,
    Replace = 2,
    Skip = 3,
    Conflict = 4,
}

public sealed record GachaPortableIndexedPlanEntry(
    GachaRecord Source,
    GachaPortableIndexedPlanKind Kind,
    IReadOnlyList<string> DifferentFields,
    GachaRecord? ExpectedTarget = null);
