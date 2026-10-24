// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Portable;

public sealed class GachaPortableSchemaTests
{
    [Theory]
    [InlineData("gacha-portable-v1-manifest.schema.json")]
    [InlineData("gacha-portable-v1-supplement.schema.json")]
    public async Task PublishedSchema_IsValidJsonAndUsesDraft202012(
        string fileName)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Schemas",
            fileName);
        await using FileStream input = File.OpenRead(path);
        using JsonDocument document = await JsonDocument.ParseAsync(input);

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            document.RootElement.GetProperty("$schema").GetString());
        Assert.True(document.RootElement.TryGetProperty("$id", out _));
        Assert.Equal(
            "object",
            document.RootElement.GetProperty("type").GetString());
    }
}
