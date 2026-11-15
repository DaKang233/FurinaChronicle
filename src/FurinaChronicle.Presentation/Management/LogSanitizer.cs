// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;

namespace FurinaChronicle.Presentation.Management;

// Sanitize crash/migration diagnostics before they touch disk. Exception type, stage and relative
// file names are preserved; absolute user paths, URLs/queries and obvious secret key-values are not.
public static partial class LogSanitizer
{
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string value = UrlRegex().Replace(text, "<url>");
        value = WindowsPathRegex().Replace(value, "<path>");
        value = UncPathRegex().Replace(value, "<path>");
        value = SecretRegex().Replace(value, "$1=<redacted>");
        return value;
    }

    public static string SanitizeException(Exception exception, string stage)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string message = Sanitize(exception.Message);
        string stack = Sanitize(exception.StackTrace);
        return $"Stage: {Sanitize(stage)}{Environment.NewLine}" +
            $"Exception: {exception.GetType().FullName}{Environment.NewLine}" +
            $"Message: {message}{Environment.NewLine}" +
            $"StackTrace: {stack}";
    }

    [GeneratedRegex(@"https?://[^\s""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:\\[^\s""']+")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"\\\\[^\s""']+")]
    private static partial Regex UncPathRegex();

    [GeneratedRegex(
        @"(?i)\b(stoken|ltoken|cookie_token|authkey|auth_key|password|secret|token|mid)\b\s*[=:]\s*[^;\s,，]+")]
    private static partial Regex SecretRegex();
}
