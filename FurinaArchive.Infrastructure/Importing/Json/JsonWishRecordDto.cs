using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FurinaArchive.Infrastructure.Importing.Json
{
    internal sealed class JsonWishRecordDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("rank_type")]
        public string? RankType { get; init; }

        [JsonPropertyName("time")]
        public string? Time { get; init; }
    }
}
