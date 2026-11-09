// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.HistoryDomains.Portable;

namespace FurinaChronicle.Infrastructure.Archives.Portable;

internal static class ArchivePortableJson
{
    public static JsonSerializerOptions Compact { get; } = Create(false);

    public static JsonSerializerOptions Display { get; } = Create(true);

    private static JsonSerializerOptions Create(bool indented)
    {
        JsonSerializerOptions options =
            ArchiveDomainPortableFileService.CreatePortableJsonOptions(indented);
        options.Converters.Add(new GachaFactReferenceJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.SnakeCaseLower,
            allowIntegerValues: false));
        return options;
    }

    private sealed class GachaFactReferenceJsonConverter
        : JsonConverter<GachaFactReference>
    {
        public override GachaFactReference Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty(
                    "game_account_id",
                    out JsonElement accountElement) ||
                !root.TryGetProperty(
                    "external_record_id",
                    out JsonElement externalElement) ||
                !accountElement.TryGetGuid(out Guid accountId) ||
                externalElement.GetString() is not string externalRecordId)
            {
                throw new JsonException(
                    "A Gacha fact reference is invalid.");
            }

            try
            {
                return new GachaFactReference(accountId, externalRecordId);
            }
            catch (ArgumentException exception)
            {
                throw new JsonException(
                    "A Gacha fact reference is invalid.",
                    exception);
            }
        }

        public override void Write(
            Utf8JsonWriter writer,
            GachaFactReference value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("game_account_id", value.GameAccountId);
            writer.WriteString("external_record_id", value.ExternalRecordId);
            writer.WriteEndObject();
        }
    }
}
