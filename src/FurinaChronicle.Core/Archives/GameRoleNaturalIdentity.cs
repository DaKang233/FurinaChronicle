// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace FurinaChronicle.Core.Archives;

public sealed record GameRoleNaturalIdentity
{
    public GameRoleNaturalIdentity(
        string gameBiz,
        string server,
        string uid)
    {
        GameBiz = NormalizeCode(gameBiz, nameof(gameBiz));
        Server = NormalizeCode(server, nameof(server));
        Uid = NormalizeUid(uid, nameof(uid));
    }

    public string GameBiz { get; }

    public string Server { get; }

    public string Uid { get; }

    public string ToDeterministicName() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{GameBiz.Length}:{GameBiz}" +
            $"{Server.Length}:{Server}" +
            $"{Uid.Length}:{Uid}");

    private static string NormalizeCode(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0 ||
            normalized.Any(
                character =>
                    !char.IsAsciiLetterOrDigit(character) &&
                    character != '_'))
        {
            throw new ArgumentException(
                "The identifier must contain only ASCII letters, digits, or underscores.",
                parameterName);
        }

        return normalized;
    }

    private static string NormalizeUid(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);

        string normalized = value.Trim();
        if (normalized.Length == 0 || !normalized.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "The UID must contain only ASCII digits.",
                parameterName);
        }

        return normalized;
    }
}
