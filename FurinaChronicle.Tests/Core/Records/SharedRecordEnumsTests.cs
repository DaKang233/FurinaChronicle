// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Tests.Core.Records;

public sealed class SharedRecordEnumsTests
{
    [Fact]
    public void DataOrigin_ContainsAcceptedValues()
    {
        Assert.Equal(
            [
                DataOrigin.Unknown,
                DataOrigin.OfficialApi,
                DataOrigin.StandardImport,
                DataOrigin.FurinaImport,
                DataOrigin.LocalObservation,
                DataOrigin.LocalCollector,
                DataOrigin.UserEntered,
                DataOrigin.Derived,
            ],
            Enum.GetValues<DataOrigin>());
    }

    [Fact]
    public void DataConfidence_ContainsAcceptedValues()
    {
        Assert.Equal(
            [
                DataConfidence.Unknown,
                DataConfidence.Low,
                DataConfidence.Medium,
                DataConfidence.High,
                DataConfidence.Confirmed,
            ],
            Enum.GetValues<DataConfidence>());
    }

    [Fact]
    public void DataCompleteness_ContainsAcceptedValues()
    {
        Assert.Equal(
            [
                DataCompleteness.Unknown,
                DataCompleteness.Partial,
                DataCompleteness.Complete,
            ],
            Enum.GetValues<DataCompleteness>());
    }

    [Fact]
    public void TimePrecision_ContainsAcceptedValues()
    {
        Assert.Equal(
            [
                TimePrecision.Second,
                TimePrecision.Minute,
                TimePrecision.Hour,
                TimePrecision.Day,
                TimePrecision.Week,
            ],
            Enum.GetValues<TimePrecision>());
    }
}
