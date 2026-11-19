// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.RegularExpressions;

namespace FurinaChronicle.Presentation.History;

/// <summary>Readable projection of official styling syntax; never changes saved evidence.</summary>
public static class HoYoDescriptionText
{
    private static readonly Regex Styles = new(
        @"</?(?:color|size|b|i)(?:=[^>]*)?>|<br\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(250));

    public static string Plain(string value) => WebUtility.HtmlDecode(Styles.Replace(
        value.Replace("\\n", "\n", StringComparison.Ordinal),
        match => match.Value.StartsWith("<br", StringComparison.OrdinalIgnoreCase) ? "\n" : string.Empty));
}
