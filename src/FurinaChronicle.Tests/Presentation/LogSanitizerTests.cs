// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.Tests.Presentation;

public sealed class LogSanitizerTests
{
    [Fact]
    public void AbsolutePathsUrlsAndSecretsAreRemoved()
    {
        string input =
            "Failed at C:\\Users\\Alice\\AppData\\Local\\FurinaChronicle\\ui1-preview.db3 " +
            "while calling https://api.example.test/path?authkey=SECRET&mid=123456789 " +
            "stoken=VERY-SECRET cookie_token=COOKIE";

        string output = LogSanitizer.Sanitize(input);

        Assert.DoesNotContain("Alice", output);
        Assert.DoesNotContain("AppData", output);
        Assert.DoesNotContain("SECRET", output);
        Assert.DoesNotContain("123456789", output);
        Assert.DoesNotContain("api.example.test", output);
        Assert.Contains("<path>", output);
        Assert.Contains("<url>", output);
        Assert.Contains("<redacted>", output);
    }

    [Fact]
    public void ExceptionSanitizationKeepsTypeAndStage()
    {
        Exception exception;
        try
        {
            throw new InvalidDataException(
                "bad C:\\Users\\Alice\\secret.db3 from https://api.test/x?token=abc");
        }
        catch (Exception caught)
        {
            exception = caught;
        }

        string output = LogSanitizer.SanitizeException(exception, "Migration/Verify");

        Assert.Contains("System.IO.InvalidDataException", output);
        Assert.Contains("Stage: Migration/Verify", output);
        Assert.DoesNotContain("Alice", output);
        Assert.DoesNotContain("secret.db3", output);
        Assert.DoesNotContain("api.test", output);
    }

    [Fact]
    public void EmptyTextIsSafe()
    {
        Assert.Equal(string.Empty, LogSanitizer.Sanitize(null));
        Assert.Equal(string.Empty, LogSanitizer.Sanitize(""));
    }
}
