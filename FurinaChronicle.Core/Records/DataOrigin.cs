// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Records;

public enum DataOrigin
{
    Unknown = 0,
    OfficialApi = 1,
    StandardImport = 2,
    FurinaImport = 3,
    LocalObservation = 4,
    LocalCollector = 5,
    UserEntered = 6,
    Derived = 7,
}
