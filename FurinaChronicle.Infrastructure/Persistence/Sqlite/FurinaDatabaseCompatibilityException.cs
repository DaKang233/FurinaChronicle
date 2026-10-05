// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class FurinaDatabaseCompatibilityException : InvalidOperationException
{
    public FurinaDatabaseCompatibilityException(
        int actualApplicationId,
        int actualSchemaVersion,
        int supportedApplicationId,
        int supportedSchemaVersion)
        : base(
            $"Cannot open the database safely. Found application_id " +
            $"0x{actualApplicationId:X8} and schema version {actualSchemaVersion}; " +
            $"this build supports application_id 0x{supportedApplicationId:X8} " +
            $"and schema version {supportedSchemaVersion}. The database was not modified.")
    {
        ActualApplicationId = actualApplicationId;
        ActualSchemaVersion = actualSchemaVersion;
        SupportedApplicationId = supportedApplicationId;
        SupportedSchemaVersion = supportedSchemaVersion;
    }

    public int ActualApplicationId { get; }

    public int ActualSchemaVersion { get; }

    public int SupportedApplicationId { get; }

    public int SupportedSchemaVersion { get; }
}
