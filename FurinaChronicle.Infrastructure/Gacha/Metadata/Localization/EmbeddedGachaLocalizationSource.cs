using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata.Localization;

public sealed class EmbeddedGachaLocalizationSource : IGachaLocalizationSource
{
    private const string ResourceSuffix =
        "Gacha.Metadata.Assets.genshin-localizations.json";

    private readonly Assembly assembly;

    public EmbeddedGachaLocalizationSource()
        : this(typeof(EmbeddedGachaLocalizationSource).Assembly)
    {
    }

    internal EmbeddedGachaLocalizationSource(Assembly assembly)
    {
        this.assembly = assembly;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> LoadAsync(
        GachaGame game,
        CancellationToken cancellationToken = default)
    {
        if (game != GachaGame.GenshinImpact)
        {
            throw new NotSupportedException(
                $"Localization source does not support game {game}.");
        }

        string? resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name =>
                name.EndsWith(ResourceSuffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new InvalidDataException(
                "The embedded Genshin localization resource is missing.");
        }

        await using Stream stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException(
                "The embedded Genshin localization resource cannot be opened.");

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

        var namesByItemId =
            new Dictionary<string, Dictionary<string, string>>(
                StringComparer.Ordinal);

        foreach (JsonProperty languageProperty in
            document.RootElement.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string language = languageProperty.Name.Trim().ToLowerInvariant();
            if (languageProperty.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty nameProperty in
                languageProperty.Value.EnumerateObject())
            {
                string? itemId = ReadItemId(nameProperty.Value);
                string localizedName = nameProperty.Name.Trim();

                if (itemId is null || localizedName.Length == 0)
                {
                    continue;
                }

                if (!namesByItemId.TryGetValue(
                    itemId,
                    out Dictionary<string, string>? names))
                {
                    names = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                    namesByItemId.Add(itemId, names);
                }

                names[language] = localizedName;
            }
        }

        return namesByItemId.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)pair.Value,
            StringComparer.Ordinal);
    }

    private static string? ReadItemId(JsonElement value)
    {
        string? itemId = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        return itemId is not null &&
            itemId.All(char.IsAsciiDigit) &&
            long.TryParse(
                itemId,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _)
            ? itemId
            : null;
    }
}
