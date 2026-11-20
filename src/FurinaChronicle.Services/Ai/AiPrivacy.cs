// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;

namespace FurinaChronicle.Services.Ai;

public static class AiPrivacy
{
    private static readonly Regex Credentials = new(@"(?:cookie|[sl]token|cookie_token|authkey|api[_-]?key|authorization)\s*[:=]|\bbearer\s+\S+|\bsk-[A-Za-z0-9_-]{12,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(200));
    private static readonly Regex Identifiers = new(@"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b|\d{8,}|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|https?://[^\s]+|[A-Za-z]:[\\/][^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(200));
    public static bool ContainsCredential(string value) => Credentials.IsMatch(value);
    public static string Sanitize(string value, IEnumerable<string>? privateIdentifiers = null, int maxLength = 16000)
    {
        if (value.Length > maxLength) throw new AiAnalysisException("文本过长，请缩短后重试。");
        if (ContainsCredential(value)) throw new AiAnalysisException("文本中疑似包含凭据，请移除后再发送。");
        string text = new(value.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
        if (privateIdentifiers is not null)
            foreach (string id in privateIdentifiers.Where(x => x.Length >= 4).OrderByDescending(x => x.Length))
                text = text.Replace(id, "[已隐藏标识]", StringComparison.OrdinalIgnoreCase);
        return Identifiers.Replace(text, "[已隐藏标识]");
    }
    public static string PublicName(string? name, IEnumerable<string> privateIdentifiers, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || ContainsCredential(name) ||
            name.Any(c => char.IsControl(c) || c is '<' or '>' or '{' or '}' or '\\' or '/' or '=')) return fallback;
        return Sanitize(name, privateIdentifiers, 80);
    }
}
