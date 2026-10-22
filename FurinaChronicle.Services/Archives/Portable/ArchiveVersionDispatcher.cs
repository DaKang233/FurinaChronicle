// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Archives.Portable;

public static class ArchiveVersionDispatcher
{
    public static ArchiveFormatVersion RequireSupportedArchive(
        string version)
    {
        ArchiveFormatVersion parsed;
        try
        {
            parsed = ArchiveFormatVersion.Parse(version);
        }
        catch (FormatException exception)
        {
            throw new ArchivePackageException(
                ArchivePackageErrorCode.UnsupportedVersion,
                "The Archive format version is invalid.",
                innerException: exception);
        }

        if (parsed.Major != ArchiveFormatVersion.Current.Major ||
            parsed.Minor > ArchiveFormatVersion.Current.Minor)
        {
            throw new ArchivePackageException(
                ArchivePackageErrorCode.UnsupportedVersion,
                $"Archive version {parsed} is not supported.");
        }

        return parsed;
    }

    public static bool IsKnownDataset(
        string format,
        string version) =>
        (format, version) switch
        {
            ("furina-archive-catalog", "1.0") => true,
            ("uigf", "v4.2") => true,
            ("furina-archive-gacha-history", "1.0") => true,
            ("furina-domain-history", "1.0") => true,
            ("furina-portable-settings", "1.0") => true,
            _ => false,
        };
}
