// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed record FurinaDatabaseDiagnostics(
    int SchemaVersion,
    int ApplicationId,
    string IntegrityCheck);
