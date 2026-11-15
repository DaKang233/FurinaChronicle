// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed class ArchivePortableContractTests
{
    [Theory]
    [InlineData("1.0", 1, 0)]
    [InlineData("0.0", 0, 0)]
    [InlineData("12.34", 12, 34)]
    public void FormatVersionRoundTrips(
        string text,
        int major,
        int minor)
    {
        ArchiveFormatVersion value = ArchiveFormatVersion.Parse(text);

        Assert.Equal(major, value.Major);
        Assert.Equal(minor, value.Minor);
        Assert.Equal(text, value.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.0.0")]
    [InlineData("v1.0")]
    [InlineData("1.-1")]
    public void InvalidFormatVersionIsRejected(string value)
    {
        Assert.ThrowsAny<Exception>(() => ArchiveFormatVersion.Parse(value));
    }

    [Fact]
    public void FutureArchiveMajorIsRejectedBeforeDatasetDispatch()
    {
        ArchivePackageException exception = Assert.Throws<ArchivePackageException>(
            () => ArchiveVersionDispatcher.RequireSupportedArchive("2.0"));

        Assert.Equal(
            ArchivePackageErrorCode.UnsupportedVersion,
            exception.Code);
    }

    [Fact]
    public void SelectedScopeRequiresAtLeastOneNonEmptyArchiveId()
    {
        Assert.Throws<ArgumentException>(() =>
            ArchiveExportScope.Selected([]));
        Assert.Throws<ArgumentException>(() =>
            ArchiveExportScope.Selected([Guid.Empty]));
    }

    [Fact]
    public void KnownDatasetVersionsAreExplicit()
    {
        Assert.True(ArchiveVersionDispatcher.IsKnownDataset(
            "furina-archive-catalog",
            "1.0"));
        Assert.True(ArchiveVersionDispatcher.IsKnownDataset("uigf", "v4.2"));
        Assert.False(ArchiveVersionDispatcher.IsKnownDataset(
            "furina-archive-catalog",
            "2.0"));
        Assert.False(ArchiveVersionDispatcher.IsKnownDataset("future", "1.0"));
    }
}
