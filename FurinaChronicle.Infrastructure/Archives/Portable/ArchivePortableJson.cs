// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Serialization;
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
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.SnakeCaseLower,
            allowIntegerValues: false));
        return options;
    }
}
