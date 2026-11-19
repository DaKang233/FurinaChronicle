// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace FurinaChronicle.Presentation.History;

public sealed record HoYoTextRun(string Text, string? Color = null, bool Bold = false,
    bool Italic = false, int? Size = null, bool Underline = false, string? GameTermId = null);

/// <summary>Safe display tokens, not HTML execution. Unknown syntax remains readable.</summary>
public static class HoYoRichText
{
    private static readonly Regex Tags = new(@"<[^>]*>|\{LINK#[A-Za-z][0-9]{1,20}\}|\{/LINK\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(250));

    public static IReadOnlyList<HoYoTextRun> Parse(string? source)
    {
        if (string.IsNullOrEmpty(source)) return [];
        try { return ParseCore(source); }
        catch (RegexMatchTimeoutException)
        {
            return [new(WebUtility.HtmlDecode(source.Replace("\\n", "\n", StringComparison.Ordinal)))];
        }
    }

    private static IReadOnlyList<HoYoTextRun> ParseCore(string source)
    {
        source = source.Replace("\\n", "\n", StringComparison.Ordinal);
        var output = new List<HoYoTextRun>();
        var stack = new List<(string Tag, HoYoTextRun Before)>();
        var style = new HoYoTextRun("");
        int position = 0;
        void Add(string text)
        {
            if (text.Length == 0) return;
            output.Add(style with { Text = WebUtility.HtmlDecode(text) });
        }
        foreach (Match match in Tags.Matches(source))
        {
            Add(source[position..match.Index]);
            if (match.Value.StartsWith("{LINK#", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count < 32)
                {
                    stack.Add(("link", style));
                    style = style with { Underline = true, GameTermId = match.Value[6..^1] };
                }
                else Add(match.Value);
                position = match.Index + match.Length;
                continue;
            }
            if (match.Value.Equals("{/LINK}", StringComparison.OrdinalIgnoreCase))
            {
                int link = stack.FindLastIndex(entry => entry.Tag == "link");
                if (link >= 0) { style = stack[link].Before; stack.RemoveRange(link, stack.Count - link); }
                else Add(match.Value);
                position = match.Index + match.Length;
                continue;
            }
            string tag = match.Value[1..^1].Trim();
            string lower = tag.ToLowerInvariant();
            if (lower is "br" or "br/" or "br /") Add("\n");
            else if (lower.StartsWith('/'))
            {
                int i = stack.FindLastIndex(entry => entry.Tag == lower[1..]);
                if (i >= 0)
                {
                    style = stack[i].Before;
                    stack.RemoveRange(i, stack.Count - i);
                }
                else Add(match.Value);
            }
            else
            {
                string kind = lower.Split('=')[0];
                HoYoTextRun? next = kind switch
                {
                    "b" when lower == "b" => style with { Bold = true },
                    "i" when lower == "i" => style with { Italic = true },
                    "u" when lower == "u" => style with { Underline = true },
                    "color" when lower.StartsWith("color=", StringComparison.Ordinal) && Color(tag[6..]) is { } color => style with { Color = color },
                    "size" when lower.StartsWith("size=", StringComparison.Ordinal) && int.TryParse(tag[5..], NumberStyles.None, CultureInfo.InvariantCulture, out int size) => style with { Size = Math.Clamp(size, 8, 40) },
                    _ => null,
                };
                if (next is not null && stack.Count < 32) { stack.Add((kind, style)); style = next; }
                else Add(match.Value);
            }
            position = match.Index + match.Length;
        }
        Add(source[position..]);
        return output;
    }

    private static string? Color(string value)
    {
        value = value.Trim(' ', '\'', '"');
        if (value.Length < 2 || value[0] != '#') return null;
        string hex = value[1..];
        if (hex.Length is not (3 or 6 or 8) || hex.Any(c => !Uri.IsHexDigit(c))) return null;
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        return "#" + hex.ToUpperInvariant();
    }
}
