// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

internal sealed class MiYouSheCharacterDetailResponseReader
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 32,
    };

    public async Task<Result> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        int httpStatus = (int)response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => CharacterWeaponCollectionStatus.AuthenticationRequired,
                HttpStatusCode.Forbidden => CharacterWeaponCollectionStatus.AccessDenied,
                HttpStatusCode.TooManyRequests => CharacterWeaponCollectionStatus.RateLimited,
                HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError => CharacterWeaponCollectionStatus.Unavailable,
                _ => CharacterWeaponCollectionStatus.SourceRejected,
            };
            return new(status, httpStatus, RetryAfter: response.Headers.RetryAfter?.Delta);
        }

        try
        {
            await response.Content.LoadIntoBufferAsync(MaximumResponseBytes, token);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream,
                new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false }, token);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("retcode", out var code) || !code.TryGetInt32(out int retcode))
                return new(CharacterWeaponCollectionStatus.InvalidResponse, httpStatus);
            if (retcode != 0)
                return new(MapRejection(retcode), httpStatus, retcode);

            if (ContainsNullArrayItem(json.RootElement))
                return new(CharacterWeaponCollectionStatus.InvalidResponse, httpStatus, retcode);
            var envelope = json.RootElement.Deserialize<Envelope>(Options);
            if (envelope?.Data is not { List: { } list } data ||
                data.PropertyMap?.Any(pair => pair.Value is not { } property ||
                    pair.Key != property.PropertyType.ToString(CultureInfo.InvariantCulture)) == true)
                return new(CharacterWeaponCollectionStatus.InvalidResponse, httpStatus, retcode);

            var items = new Dictionary<int, Entry>();
            foreach (var item in list)
            {
                if (item?.Base is not { Id: > 0, Level: >= 0, ActivedConstellationNum: >= 0 and <= 6 } basic ||
                    !items.TryAdd(basic.Id, new(basic.Level!.Value, basic.ActivedConstellationNum!.Value, Map(item))))
                    return new(CharacterWeaponCollectionStatus.InvalidResponse, httpStatus, retcode);
            }

            var catalog = new CharacterResponseMetadata(
                data.PropertyMap?.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair =>
                    new CharacterPropertyMetadata(pair.Value!.PropertyType, pair.Value.Name, pair.Value.Icon, pair.Value.FilterName),
                    StringComparer.Ordinal),
                data.RelicPropertyOptions?.ToDomain(), data.RelicWiki, data.WeaponWiki, data.AvatarWiki);
            return new(CharacterWeaponCollectionStatus.Received, httpStatus, retcode, items, catalog);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or HttpRequestException or InvalidOperationException)
        {
            return new(CharacterWeaponCollectionStatus.InvalidResponse, httpStatus);
        }
    }

    internal static bool ContainsNullArrayItem(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Null || ContainsNullArrayItem(item)),
        JsonValueKind.Object => value.EnumerateObject().Any(property => ContainsNullArrayItem(property.Value)),
        _ => false,
    };

    private static CharacterWeaponCollectionStatus MapRejection(int code) => code switch
    {
        -100 or 1004 or 10001 => CharacterWeaponCollectionStatus.AuthenticationRequired,
        1034 or 5003 or 10306 => CharacterWeaponCollectionStatus.VerificationRequired,
        -110 or 10101 => CharacterWeaponCollectionStatus.RateLimited,
        10102 => CharacterWeaponCollectionStatus.AccessDenied,
        _ => CharacterWeaponCollectionStatus.SourceRejected,
    };

    private static CharacterDetail Map(Detail value)
    {
        Character basic = value.Base!;
        if (basic.Fetter is < 0 || basic.PromoteLevel is < 0 or > 6 ||
            IsInvalidWeapon(value.Weapon) || IsInvalidWeapon(basic.Weapon) ||
            value.Relics?.Any(relic => relic.Id <= 0 || relic.Level is < 0 || relic.MainProperty?.Times is < 0 ||
                relic.SubPropertyList?.Any(stat => stat.Times is < 0) == true) == true ||
            value.Constellations?.Any(constellation => constellation.Id <= 0 || constellation.Pos is < 1 or > 6) == true ||
            value.Skills?.Any(skill => skill.SkillId <= 0 || skill.Level is < 0) == true)
            throw new ArgumentException("Invalid official character detail.");

        return new(basic.Name, basic.Element, basic.Rarity, basic.Fetter, basic.PromoteLevel,
            basic.Icon, basic.SideIcon, basic.Image, basic.CardImage, basic.IsChosen, basic.WeaponType,
            (value.Weapon ?? basic.Weapon)?.ToDomain(),
            value.Constellations?.Select(item =>
                new CharacterConstellation(item.Id, item.Pos, item.Name, item.Icon, item.IsActived, item.Effect,
                    MapEnhancement(item.CanEnhanced, item.IsEnhanced, item.EnhancedEffect))).ToArray(),
            value.Skills?.Select(item => new CharacterTalent(item.SkillId, item.SkillType, item.Level,
                item.Name, item.Icon, item.IsUnlock, item.Desc, item.SkillAffixList?.Select(affix =>
                    new CharacterTalentValue(affix.Name, affix.Value)).ToArray(),
                MapEnhancement(item.CanEnhanced, item.IsEnhanced, item.EnhancedDesc),
                item.BeforeEnhancedSkillAttrIndex, item.AfterEnhancedSkillAttrIndex)).ToArray(),
            MapProperties(value.SelectedProperties), MapProperties(value.BaseProperties),
            MapProperties(value.ExtraProperties), MapProperties(value.ElementProperties),
            value.Relics?.Select(item => item.ToDomain()).ToArray(),
            value.Costumes?.Select(item => new CharacterCostume(item.Id, item.Name, item.Icon)).ToArray(),
            value.RecommendRelicProperty is { } recommendation ? new(
                recommendation.RecommendProperties?.ToDomain(), recommendation.CustomProperties?.ToDomain(),
                recommendation.HasSetRecommendProp) : null,
            value.UnlockTps, value.WeaponSkin is { } skin ? new(
                skin.WeaponSkinName, skin.WeaponSkinIcon, skin.WeaponSkinRarity) : null,
            basic.Weapon?.ToDomain());
    }

    internal static bool IsInvalidWeapon(Weapon? weapon) => weapon is { Id: <= 0 } or
        { Level: < 0 } or { AffixLevel: < 0 } or { PromoteLevel: < 0 or > 6 };

    private static CharacterEnhancement? MapEnhancement(bool? canEnhance, bool? isEnhanced, string? description) =>
        canEnhance is null && isEnhanced is null && description is null ? null :
            new CharacterEnhancement(canEnhance, isEnhanced, description);

    private static CharacterStat[]? MapProperties(Stat[]? properties) =>
        properties?.Select(item => item.ToDomain()).ToArray();

    internal sealed record Entry(int Level, int ConstellationCount, CharacterDetail Detail);
    internal sealed record Result(CharacterWeaponCollectionStatus Status, int HttpStatus, int? ProviderCode = null,
        IReadOnlyDictionary<int, Entry>? Entries = null, CharacterResponseMetadata? Metadata = null,
        TimeSpan? RetryAfter = null);

    private sealed record Envelope(int Retcode, string? Message, Payload? Data);
    private sealed record Payload(Detail?[]? List, Dictionary<string, PropertyMetadata?>? PropertyMap,
        RecommendedStats? RelicPropertyOptions, Dictionary<string, string>? RelicWiki,
        Dictionary<string, string>? WeaponWiki, Dictionary<string, string>? AvatarWiki);
    private sealed record PropertyMetadata([property: JsonRequired] int PropertyType, string? Name, string? Icon, string? FilterName);
    internal sealed record Character(int Id, int? Level, int? ActivedConstellationNum,
        string? Name, string? Element, int? Rarity, int? Fetter, int? PromoteLevel, int? WeaponType,
        string? Icon, string? SideIcon, string? Image, string? CardImage, bool? IsChosen, Weapon? Weapon)
    {
        public CharacterRosterEntry ToRoster() => new(Level!.Value, ActivedConstellationNum!.Value,
            Name, Element, Rarity, Fetter, PromoteLevel, WeaponType, Icon, SideIcon, Image, CardImage, IsChosen,
            Weapon?.ToDomain());
    }
    private sealed record Detail([property: JsonPropertyName("base")] Character? Base,
        Weapon? Weapon, Relic[]? Relics, Constellation[]? Constellations, Costume[]? Costumes,
        Stat[]? SelectedProperties, Stat[]? BaseProperties, Stat[]? ExtraProperties, Stat[]? ElementProperties,
        Skill[]? Skills, Recommendation? RecommendRelicProperty, bool? UnlockTps, Skin? WeaponSkin);
    internal sealed record Weapon(int Id, string? Name, string? Icon, int? Type, string? TypeName,
        int? Rarity, int? Level, int? AffixLevel, int? PromoteLevel, string? Desc, Stat? MainProperty, Stat? SubProperty)
    {
        public CharacterWeaponDetail ToDomain() => new(Id, Name, Icon, Type, TypeName, Rarity, Level,
            AffixLevel, PromoteLevel, Desc, MainProperty?.ToDomain(), SubProperty?.ToDomain());
    }

    private sealed record Skin(string? WeaponSkinName, string? WeaponSkinIcon, int? WeaponSkinRarity);
    private sealed record Costume([property: JsonRequired] int Id, string? Name, string? Icon);
    private sealed record Constellation(int Id, string? Name, string? Icon, string? Effect, bool? IsActived, int? Pos,
        bool? CanEnhanced, bool? IsEnhanced, string? EnhancedEffect);
    private sealed record Skill(int SkillId, int? SkillType, int? Level, string? Desc, SkillAffix[]? SkillAffixList,
        string? Icon, bool? IsUnlock, string? Name, bool? CanEnhanced, bool? IsEnhanced, string? EnhancedDesc,
        int[]? BeforeEnhancedSkillAttrIndex, int[]? AfterEnhancedSkillAttrIndex);
    private sealed record SkillAffix(string? Name, string? Value);
    internal sealed record Stat([property: JsonRequired] int PropertyType, string? Base, string? Add, string? Final)
    {
        public CharacterStat ToDomain() => new(PropertyType, Base, Add, Final);
    }

    private sealed record Relic(int Id, string? Name, string? Icon, int? Pos, string? PosName, int? Rarity,
        int? Level, RelicSet? Set, RelicStat? MainProperty, RelicStat[]? SubPropertyList)
    {
        public CharacterArtifact ToDomain() => new(Id, Pos, PosName, Name, Icon, Rarity, Level,
            Set is { } set ? new(set.Id, set.Name,
                set.Affixes?.Select(item => new CharacterSetBonus(item.ActivationNumber, item.Effect)).ToArray()) : null,
            MainProperty?.ToDomain(), SubPropertyList?.Select(item => item.ToDomain()).ToArray());
    }

    private sealed record RelicSet([property: JsonRequired] int Id, string? Name, RelicAffix[]? Affixes);
    private sealed record RelicAffix(int? ActivationNumber, string? Effect);
    private sealed record RelicStat([property: JsonRequired] int PropertyType, string? Value, int? Times)
    {
        public CharacterArtifactStat ToDomain() => new(PropertyType, Value, Times);
    }

    private sealed record Recommendation(RecommendedStats? RecommendProperties, RecommendedStats? CustomProperties,
        bool? HasSetRecommendProp);
    private sealed record RecommendedStats(int[]? SandMainPropertyList, int[]? GobletMainPropertyList,
        int[]? CircletMainPropertyList, int[]? SubPropertyList)
    {
        public CharacterRecommendedStats ToDomain() => new(SandMainPropertyList, GobletMainPropertyList,
            CircletMainPropertyList, SubPropertyList);
    }
}
