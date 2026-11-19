// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.History;

namespace FurinaChronicle.Tests.Presentation.History;

public sealed class HoYoRichTextTests
{
    [Fact]
    public void NestedColorAndEmphasisRestoreTheOuterStyle()
    {
        var runs = HoYoRichText.Parse("A<color=#FFD780FF>B<b>C</b>D</color>E");
        Assert.Equal("ABCDE", string.Concat(runs.Select(run => run.Text)));
        Assert.Null(runs[0].Color); Assert.Equal("#FFD780FF", runs[1].Color);
        Assert.True(runs[2].Bold); Assert.False(runs[3].Bold); Assert.Null(runs[4].Color);
    }

    [Fact]
    public void UnderlineCanNestWithColorBoldAndItalicAndRestoresAfterClosing()
    {
        const string source = "A<u>B<color=#FFD780FF><b><i>C</i></b></color>D</u>E";
        var runs = HoYoRichText.Parse(source);
        Assert.Equal("ABCDE", string.Concat(runs.Select(run => run.Text)));
        Assert.False(runs[0].Underline); Assert.True(runs[1].Underline);
        Assert.True(runs[2].Underline); Assert.True(runs[2].Bold); Assert.True(runs[2].Italic);
        Assert.Equal("#FFD780FF", runs[2].Color);
        Assert.True(runs[3].Underline); Assert.Null(runs[3].Color); Assert.False(runs[4].Underline);
        Assert.Equal("ABCDE", HoYoDescriptionText.Plain(source));
    }

    [Fact]
    public void OfficialGameTermSyntaxShowsUnderlinedTextAndRetainsInternalReference()
    {
        const string source = "{LINK#N211101}电涌{/LINK}";
        var run = Assert.Single(HoYoRichText.Parse(source));
        Assert.Equal("电涌", run.Text); Assert.True(run.Underline);
        Assert.Equal("N211101", run.GameTermId);
        Assert.Equal("电涌", HoYoDescriptionText.Plain(source));
    }

    [Fact]
    public void GameTermsCanContainStylesWithoutBleedingReferenceOrUnderlineIntoFollowingText()
    {
        const string source = "触发{LINK#N211101}<color=#FFD780FF><b>电涌</b></color>{/LINK}反应";
        var runs = HoYoRichText.Parse(source);
        var term = Assert.Single(runs, run => run.Text == "电涌");
        Assert.True(term.Underline); Assert.True(term.Bold); Assert.Equal("#FFD780FF", term.Color);
        Assert.Equal("N211101", term.GameTermId);
        Assert.Null(runs[^1].GameTermId); Assert.False(runs[^1].Underline);
        Assert.Equal("触发电涌反应", HoYoDescriptionText.Plain(source));
    }

    [Fact]
    public void UnknownTermSyntaxRemainsLiteralInsteadOfBecomingAnExternalLink()
    {
        const string source = "{LINK#https://example.invalid}术语{/LINK}";
        Assert.Equal(source, HoYoDescriptionText.Plain(source));
        Assert.All(HoYoRichText.Parse(source), run => Assert.Null(run.GameTermId));
    }

    [Fact]
    public void EntitiesAndNewlinesRemainReadableAndEncodedMarkupIsLiteral()
    {
        var runs = HoYoRichText.Parse("<i>A &amp; B</i>\\nC<br/>D &lt;b&gt;E&lt;/b&gt;");
        Assert.Equal("A & B\nC\nD <b>E</b>", string.Concat(runs.Select(run => run.Text)));
        Assert.True(runs[0].Italic);
    }

    [Theory]
    [InlineData("#fff", "#FFFFFF")]
    [InlineData("#12ab34", "#12AB34")]
    [InlineData("#11223344", "#11223344")]
    public void ValidSourceColorsArePreserved(string source, string expected) =>
        Assert.Equal(expected, Assert.Single(HoYoRichText.Parse($"<color={source}>效果</color>")).Color);

    [Fact]
    public void UnknownOrInvalidTagsRemainTextAndCannotCreateExecutableContent()
    {
        const string input = "<script>x</script><color=javascript:bad>Y</color><未知标记>Z";
        Assert.Equal(input, string.Concat(HoYoRichText.Parse(input).Select(run => run.Text)));
    }

    [Fact]
    public void FontSizesAndNestingHaveBoundsWithoutDroppingText()
    {
        Assert.Equal(40, Assert.Single(HoYoRichText.Parse("<size=999>大字</size>")).Size);
        var runs = HoYoRichText.Parse(string.Concat(Enumerable.Repeat("<b>", 200)) + "内容" + string.Concat(Enumerable.Repeat("</b>", 200)));
        Assert.Contains("内容", string.Concat(runs.Select(run => run.Text)));
    }
}
