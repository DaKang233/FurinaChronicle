// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

/// <summary>
/// Maps a current-season Game Record response into the narrow history model.
/// Request construction and authentication belong to the transport.
/// </summary>
public sealed class MiYouSheSpiralAbyssResponseReader(TimeProvider timeProvider)
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        MaxDepth = 32,
    };

    public async Task<SpiralAbyssSourceResult> ReadAsync(
        HttpResponseMessage response,
        GameRoleNaturalIdentity expectedRole,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccessStatusCode)
        {
            SpiralAbyssSourceResult rejected = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new(SpiralAbyssCollectionStatus.AuthenticationRequired),
                HttpStatusCode.Forbidden => new(SpiralAbyssCollectionStatus.AccessDenied),
                HttpStatusCode.TooManyRequests => new(
                    SpiralAbyssCollectionStatus.RateLimited,
                    RetryAfter: GetRetryDelay(response)),
                HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError =>
                    new(SpiralAbyssCollectionStatus.Unavailable),
                _ => new(SpiralAbyssCollectionStatus.SourceRejected),
            };
            return rejected with { HttpStatus = (int)response.StatusCode };
        }

        try
        {
            await response.Content.LoadIntoBufferAsync(
                MaxResponseBytes, cancellationToken);
            DateTimeOffset fetchedAt = timeProvider.GetUtcNow();
            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            Envelope? envelope = await JsonSerializer.DeserializeAsync<Envelope>(
                body, JsonOptions, cancellationToken);
            return Map(envelope, fetchedAt, expectedRole) with { HttpStatus = (int)response.StatusCode };
        }
        catch (JsonException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        catch (ArgumentException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        catch (HttpRequestException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
    }

    private TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan? delay = retryAfter?.Delta ??
            (retryAfter?.Date - timeProvider.GetUtcNow());
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static SpiralAbyssSourceResult Map(
        Envelope? envelope, DateTimeOffset fetchedAt, GameRoleNaturalIdentity expectedRole)
    {
        if (envelope?.Retcode is not int retcode)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        if (retcode != 0)
        {
            SpiralAbyssSourceResult rejected = retcode switch
            {
                -100 or 1004 or 10001 => new(SpiralAbyssCollectionStatus.AuthenticationRequired),
                1034 => new(SpiralAbyssCollectionStatus.VerificationRequired),
                -110 or 10101 => new(SpiralAbyssCollectionStatus.RateLimited),
                -1 => new(SpiralAbyssCollectionStatus.Empty),
                5003 or 10102 => new(SpiralAbyssCollectionStatus.AccessDenied),
                _ => new(SpiralAbyssCollectionStatus.SourceRejected),
            };
            return rejected with { ProviderCode = retcode };
        }
        if (envelope.Data is not { } data)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        string? responseUid = data.Uid.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => data.Uid.GetString(),
            JsonValueKind.Number => data.Uid.GetRawText(),
            _ => string.Empty,
        };
        if (responseUid is not null &&
            !string.Equals(responseUid, expectedRole.Uid, StringComparison.Ordinal))
        {
            return new(SpiralAbyssCollectionStatus.ResponseRoleMismatch);
        }
        if (data.IsUnlock is false)
        {
            return new(SpiralAbyssCollectionStatus.NotUnlocked);
        }
        if (data.Floors is not { } floors || data.ScheduleId is not > 0)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        if (floors.Length == 0)
        {
            return new(SpiralAbyssCollectionStatus.Empty);
        }

        var mappedFloors = new List<SpiralAbyssFloorResult>(floors.Length);
        foreach (Floor? floor in floors)
        {
            if (floor is not { Index: int floorIndex, Levels: { } levels } ||
                floorIndex <= 0)
            {
                return new(SpiralAbyssCollectionStatus.InvalidResponse);
            }

            var chambers = new List<SpiralAbyssChamberResult>(levels.Length);
            foreach (Level? level in levels)
            {
                if (level is not { Index: int chamberIndex, Star: int stars })
                {
                    return new(SpiralAbyssCollectionStatus.InvalidResponse);
                }
                if (chamberIndex <= 0 || stars is < 0 or > 3)
                {
                    return new(SpiralAbyssCollectionStatus.InvalidResponse);
                }

                // A zero-star slot alone cannot distinguish a clear from a
                // placeholder. Domain chamber results are positive clear evidence.
                if (stars > 0)
                {
                    chambers.Add(new SpiralAbyssChamberResult(
                        chamberIndex, stars, OccurrenceTime.Unknown));
                }
            }
            mappedFloors.Add(new SpiralAbyssFloorResult(
                floorIndex, DataCompleteness.Partial, chambers));
        }

        DateTimeOffset? periodStart = ReadUnixTime(data.StartTime);
        DateTimeOffset? periodEnd = ReadUnixTime(data.EndTime);
        if (periodStart > periodEnd)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        return new(
            SpiralAbyssCollectionStatus.Received,
            new SpiralAbyssSnapshot(
                data.ScheduleId.Value.ToString(CultureInfo.InvariantCulture),
                periodStart,
                periodEnd,
                mappedFloors,
                fetchedAt));
    }

    private static DateTimeOffset? ReadUnixTime(long? seconds) =>
        seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;

    private sealed record Envelope(int? Retcode, AbyssData? Data);
    private sealed record AbyssData(int? ScheduleId, long? StartTime, long? EndTime, Floor?[]? Floors, JsonElement Uid, bool? IsUnlock);
    private sealed record Floor(int? Index, Level?[]? Levels);
    private sealed record Level(int? Index, int? Star);
}
