// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;

namespace FurinaChronicle.Infrastructure.Metadata;

public sealed partial class SnapMetadataClient
{
    private static readonly Regex SkillParameter = new(@"\{param([1-9]\d{0,2}):(F[0-3]P?|P|I)\}",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public async Task<CharacterMechanicsMetadata?> ResolveCharacterMechanicsAsync(
        string typeId, IReadOnlyList<CharacterTalent> observedTalents, CancellationToken cancellationToken)
    {
        if (!int.TryParse(typeId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
            return null;
        var avatar = await ReadJsonAsync<MechanicsAvatar>($"Avatar/{id}.json", cancellationToken);
        if (avatar?.SkillDepot is not { } depot)
            return null;

        var talents = (depot.Skills ?? []).Concat(depot.EnergySkill is { } energy ? [energy] : [])
            .Concat(depot.SpecialSkills ?? []).Concat(depot.Inherents ?? []);
        var levels = observedTalents.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First().Level);
        return new(talents.Where(item => item is { Id: > 0 }).DistinctBy(item => item.Id).Select(item =>
            new CharacterTalent(item.Id, null, null, item.Name, IconUrl(item.Icon), null, item.Description,
                ReadSkillValues(item.Proud, levels.GetValueOrDefault(item.Id)))).ToArray(),
            (depot.Talents ?? []).Select((item, index) =>
                new CharacterConstellation(item.Id, index + 1, item.Name, IconUrl(item.Icon), null, item.Description)).ToArray());
    }

    private static IReadOnlyList<CharacterTalentValue>? ReadSkillValues(MechanicsProud? proud, int? level)
    {
        if (level is null || proud?.Descriptions is not { } descriptions ||
            proud.Parameters?.FirstOrDefault(item => item.Level == level)?.Parameters is not { } values)
            return null;

        var output = new List<CharacterTalentValue>();
        foreach (var description in descriptions)
        {
            int split = description.IndexOf('|');
            if (split <= 0 || split == description.Length - 1) continue;
            string result = SkillParameter.Replace(description[(split + 1)..], match =>
            {
                int index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                if (index >= values.Length) return match.Value;
                decimal value = values[index];
                string format = match.Groups[2].Value;
                if (format.EndsWith('P'))
                {
                    string digits = format.Length == 3 ? format[1..2] : "0";
                    return (value * 100).ToString("F" + digits, CultureInfo.InvariantCulture) + "%";
                }
                return value.ToString(format == "I" ? "F0" : format, CultureInfo.InvariantCulture);
            });
            if (!result.Contains("{param", StringComparison.Ordinal))
                output.Add(new(description[..split].TrimStart('#'), result.TrimStart('#')));
        }
        return output.Count == 0 ? null : output;
    }

    private sealed record MechanicsAvatar(MechanicsDepot? SkillDepot);
    private sealed record MechanicsDepot(MechanicsSkill[]? Skills, MechanicsSkill? EnergySkill,
        MechanicsSkill[]? SpecialSkills, MechanicsSkill[]? Inherents, MechanicsSkill[]? Talents);
    private sealed record MechanicsSkill(int Id, string? Name, string? Icon, string? Description, MechanicsProud? Proud);
    private sealed record MechanicsProud(string[]? Descriptions, MechanicsLevel[]? Parameters);
    private sealed record MechanicsLevel(int Level, decimal[]? Parameters);
}
