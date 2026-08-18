using FurinaChronicle.Infrastructure.Importing.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FurinaChronicle.Infrastructure.Importing.Json
{
    internal sealed class JsonWishImportDto
    {
        [JsonPropertyName("list")]
        public List<JsonWishRecordDto?>? List { get; init; }
    }
}
