// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Challenges;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    // Theater statistics previously embedded this Core shape. Preserve its exact legacy
    // null fields and property order, while omitting the new unknown constellation.
    private sealed class DomainChallengeParticipantConverter : JsonConverter<ChallengeParticipant>
    {
        public override ChallengeParticipant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = JsonSerializer.Deserialize<EmbeddedParticipantPayload>(ref reader, options)
                ?? throw new JsonException("A challenge participant is missing.");
            return new(value.Id, value.Name, value.IconUrl, value.Level, value.Rarity, value.Origin, value.Constellation);
        }

        public override void Write(Utf8JsonWriter writer, ChallengeParticipant value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new EmbeddedParticipantPayload(
                value.Id, value.Name, value.IconUrl, value.Level, value.Rarity, value.Origin, value.Constellation), options);
    }

    private sealed record EmbeddedParticipantPayload(
        string Id, string? Name, string? IconUrl, int? Level, int? Rarity, ChallengeParticipantOrigin? Origin,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Constellation);
}
