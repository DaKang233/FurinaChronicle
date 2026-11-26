// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

public sealed partial class HoYoGameRecordCharacterWeaponSource
{
    private const string CalculatorRosterUrl =
        "https://api-takumi.mihoyo.com/event/e20200928calculate/v1/sync/avatar/list";
    private const int CalculatorPageSize = 20;
    private const int CalculatorMaximumPages = 20;

    private static readonly JsonSerializerOptions CalculatorJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 20,
    };

    private async Task<IReadOnlyDictionary<int, CharacterAscensionEvidence>> FetchAscensionAsync(
        PassportAccount passport, GameRoleNaturalIdentity role, CancellationToken token)
    {
        if (passport.Credentials.CookieToken is not { } credential || !IsHeaderToken(credential))
            return new Dictionary<int, CharacterAscensionEvidence>();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var entries = new Dictionary<int, CharacterAscensionEvidence>();
        try
        {
            int? total = null;
            for (int page = 1; page <= CalculatorMaximumPages; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, CalculatorRosterUrl)
                {
                    Content = JsonContent.Create(new
                    {
                        uid = role.Uid, region = role.Server, lang = "zh-cn", page, size = CalculatorPageSize,
                        element_attr_ids = Array.Empty<int>(), weapon_cat_ids = Array.Empty<int>(),
                    }),
                };
                request.Headers.Referrer = new Uri("https://webstatic.mihoyo.com/");
                request.Headers.Add("Cookie", $"account_id={passport.Aid}; cookie_token={credential}");
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (!response.IsSuccessStatusCode) break;
                await response.Content.LoadIntoBufferAsync(1024 * 1024, deadline.Token);
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var document = await JsonDocument.ParseAsync(stream,
                    new JsonDocumentOptions { MaxDepth = 20, AllowDuplicateProperties = false }, deadline.Token);
                if (MiYouSheCharacterDetailResponseReader.ContainsNullArrayItem(document.RootElement)) break;
                var result = document.RootElement.Deserialize<CalculatorEnvelope>(CalculatorJson);
                if (result is not { Retcode: 0, Data: { List: { } list } payload } ||
                    list.Length > CalculatorPageSize || payload.Total < 0 ||
                    total is not null && payload.Total != total)
                    break;

                total = payload.Total;
                foreach (var item in list)
                {
                    if (item is not { Id: > 0 } || item.PromoteLevel is < 0 or > 6 || !entries.TryAdd(item.Id, item.ToEvidence()))
                        return new Dictionary<int, CharacterAscensionEvidence>();
                }

                if (total is int expected && entries.Count == expected || total is null && list.Length < CalculatorPageSize)
                    return entries;
                if (list.Length < CalculatorPageSize || total is int count && entries.Count > count)
                    break;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or ArgumentException) { }

        return new Dictionary<int, CharacterAscensionEvidence>();
    }

    private sealed record CalculatorEnvelope(int? Retcode, string? Message, CalculatorPayload? Data);
    private sealed record CalculatorPayload(CalculatorEntry?[]? List, int? Total);
    private sealed record CalculatorEntry(int Id, string? Name, string? Icon, int? WeaponCatId, int? AvatarLevel,
        int? ElementAttrId, int? MaxLevel, int? LevelCurrent, int? LevelTarget, int? PromoteLevel,
        CalculatorSkill[]? SkillList, CalculatorWeapon? Weapon, CalculatorRelic[]? ReliquaryList,
        string? WikiUrl, string? WikiRecommendWeaponUrl, int? ConstellationNum, int? FetterLevel)
    {
        public CharacterAscensionEvidence ToEvidence() => new(Name, Icon, WeaponCatId, AvatarLevel, ElementAttrId,
            MaxLevel, LevelCurrent, LevelTarget, PromoteLevel, ConstellationNum, FetterLevel, WikiUrl, WikiRecommendWeaponUrl,
            SkillList?.Select(item => new CharacterCalculatorSkill(item.Id, item.GroupId, item.Name, item.Icon, item.LevelCurrent,
                item.LevelTarget, item.MaxLevel)).ToArray(),
            Weapon is { } weapon ? new(weapon.Id, weapon.Name, weapon.Icon, weapon.WeaponCatId,
                weapon.WeaponLevel, weapon.LevelCurrent, weapon.LevelTarget, weapon.MaxLevel) : null,
            ReliquaryList?.Select(item => new CharacterCalculatorArtifact(item.Id, item.Name, item.Icon,
                item.ReliquaryCatId, item.ReliquaryLevel, item.LevelCurrent, item.LevelTarget, item.MaxLevel)).ToArray());
    }

    private sealed record CalculatorSkill([property: JsonRequired] int Id, int? GroupId, string? Name, string? Icon, int? LevelCurrent, int? LevelTarget, int? MaxLevel);
    private sealed record CalculatorWeapon([property: JsonRequired] int Id, string? Name, string? Icon, int? WeaponCatId, int? WeaponLevel, int? LevelCurrent, int? LevelTarget, int? MaxLevel);
    private sealed record CalculatorRelic([property: JsonRequired] int Id, string? Name, string? Icon, int? ReliquaryCatId, int? ReliquaryLevel, int? LevelCurrent, int? LevelTarget, int? MaxLevel);
}
