// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FurinaChronicle.Infrastructure.Importing.Json
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
