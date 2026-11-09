// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Importing.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FurinaChronicle.Infrastructure.Importing.Json
{
    internal sealed class JsonGachaImportDto
    {
        [JsonPropertyName("list")]
        public List<JsonGachaRecordDto?>? List { get; init; }
    }
}
