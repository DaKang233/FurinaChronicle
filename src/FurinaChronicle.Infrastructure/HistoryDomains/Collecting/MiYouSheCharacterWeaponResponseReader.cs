// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

/// <summary>
/// Maps the official character list into the narrow inventory history model.
/// Characters come from a complete roster; weapons only cover the equipped
/// copies, so they are observed as an aggregate with an explicit partial scope.
/// </summary>
public sealed class MiYouSheCharacterWeaponResponseReader(TimeProvider timeProvider)
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        MaxDepth = 32,
    };

    public async Task<CharacterWeaponSourceResult> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccessStatusCode)
        {
            CharacterWeaponCollectionStatus status = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    CharacterWeaponCollectionStatus.AuthenticationRequired,
                HttpStatusCode.Forbidden =>
                    CharacterWeaponCollectionStatus.AccessDenied,
                HttpStatusCode.TooManyRequests =>
                    CharacterWeaponCollectionStatus.RateLimited,
                HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError =>
                    CharacterWeaponCollectionStatus.Unavailable,
                _ => CharacterWeaponCollectionStatus.SourceRejected,
            };
            return new(
                status,
                RetryAfter: GetRetryDelay(response),
                HttpStatus: (int)response.StatusCode);
        }

        try
        {
            await response.Content.LoadIntoBufferAsync(
                MaxResponseBytes, cancellationToken);
            DateTimeOffset fetchedAt = timeProvider.GetUtcNow();
            await using Stream body = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            Envelope? envelope = await JsonSerializer.DeserializeAsync<Envelope>(
                body, JsonOptions, cancellationToken);
            CharacterWeaponSourceResult mapped = Map(envelope, fetchedAt);
            return mapped with { HttpStatus = (int)response.StatusCode };
        }
        catch (JsonException)
        {
            return new(CharacterWeaponCollectionStatus.InvalidResponse);
        }
        catch (ArgumentException)
        {
            return new(CharacterWeaponCollectionStatus.InvalidResponse);
        }
        catch (HttpRequestException)
        {
            return new(CharacterWeaponCollectionStatus.InvalidResponse);
        }
    }

    private TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan? delay = retryAfter?.Delta ??
            (retryAfter?.Date - timeProvider.GetUtcNow());
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static CharacterWeaponSourceResult Map(
        Envelope? envelope,
        DateTimeOffset fetchedAt)
    {
        if (envelope?.Retcode is not int retcode)
        {
            return new(CharacterWeaponCollectionStatus.InvalidResponse);
        }
        if (retcode != 0)
        {
            CharacterWeaponSourceResult rejected = retcode switch
            {
                -100 or 1004 or 10001 =>
                    new(CharacterWeaponCollectionStatus.AuthenticationRequired),
                1034 => new(CharacterWeaponCollectionStatus.VerificationRequired),
                -110 or 10101 => new(CharacterWeaponCollectionStatus.RateLimited),
                5003 or 10102 => new(CharacterWeaponCollectionStatus.AccessDenied),
                _ => new(CharacterWeaponCollectionStatus.SourceRejected),
            };
            return rejected with { ProviderCode = retcode };
        }
        if (envelope.Data?.List is not { } characters)
        {
            return new(CharacterWeaponCollectionStatus.InvalidResponse);
        }
        if (characters.Length == 0)
        {
            return new(CharacterWeaponCollectionStatus.Empty);
        }

        var characterEntries = new List<InventoryStateEntry>(characters.Length);
        var equipped = new Dictionary<int, List<WeaponCopy>>();
        foreach (Character? character in characters)
        {
            if (character is not { Id: > 0 } ||
                character.Level is not >= 0 ||
                character.ActivedConstellationNum is not >= 0)
            {
                return new(CharacterWeaponCollectionStatus.InvalidResponse);
            }

            characterEntries.Add(new InventoryStateEntry(
                InventoryItemKind.Character,
                character.Id.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                instanceId: null,
                quantity: 1,
                level: character.Level,
                progression: character.ActivedConstellationNum,
                InventoryFieldCoverage.Level | InventoryFieldCoverage.Progression,
                DataCompleteness.Complete));

            if (character.Weapon is not { } weapon)
            {
                continue;
            }
            if (weapon.Id is not > 0 ||
                weapon.Level is not >= 0 ||
                weapon.AffixLevel is not >= 0)
            {
                return new(CharacterWeaponCollectionStatus.InvalidResponse);
            }

            if (!equipped.TryGetValue(weapon.Id.Value, out List<WeaponCopy>? copies))
            {
                copies = [];
                equipped[weapon.Id.Value] = copies;
            }
            copies.Add(new WeaponCopy(weapon.Level.Value, weapon.AffixLevel.Value));
        }

        var weaponEntries = new List<InventoryStateEntry>(equipped.Count);
        foreach ((int weaponId, List<WeaponCopy> copies) in equipped
            .OrderBy(pair => pair.Key))
        {
            int? level = copies.All(copy => copy.Level == copies[0].Level)
                ? copies[0].Level
                : null;
            int? refinement = copies.All(
                copy => copy.Refinement == copies[0].Refinement)
                ? copies[0].Refinement
                : null;
            InventoryFieldCoverage coverage = InventoryFieldCoverage.Quantity |
                (level is not null ? InventoryFieldCoverage.Level : 0) |
                (refinement is not null ? InventoryFieldCoverage.Progression : 0);
            weaponEntries.Add(new InventoryStateEntry(
                InventoryItemKind.Weapon,
                weaponId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                instanceId: null,
                quantity: copies.Count,
                level,
                refinement,
                coverage,
                DataCompleteness.Complete));
        }

        return new(
            CharacterWeaponCollectionStatus.Received,
            new CharacterWeaponSnapshot(
                characterEntries,
                DataCompleteness.Complete,
                weaponEntries,
                DataCompleteness.Partial,
                fetchedAt));
    }

    private sealed record Envelope(int? Retcode, CharacterList? Data);

    private sealed record CharacterList(Character?[]? List);

    private sealed record Character(
        int? Id,
        int? Level,
        int? ActivedConstellationNum,
        Weapon? Weapon);

    private sealed record Weapon(int? Id, int? Level, int? AffixLevel);

    private readonly record struct WeaponCopy(int Level, int Refinement);
}
