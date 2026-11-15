// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace FurinaChronicle.Services.Gacha.History;

public static class GachaCorrectionTime
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mmzzz",
    ];

    public static bool TryParse(string input, out DateTimeOffset time)
    {
        string text = input.Trim();
        if (text.EndsWith('Z'))
        {
            text = text[..^1] + "+00:00";
        }
        return DateTimeOffset.TryParseExact(
            text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }
}
