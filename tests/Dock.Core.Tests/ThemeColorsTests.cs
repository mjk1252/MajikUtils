using Dock.Core.Models;

namespace Dock.Core.Tests;

/// <summary>
/// What the three colour boxes in Settings accept, and what they do with everything else.
///
/// The rejections matter more than the acceptances here. These boxes are read on every keystroke,
/// so most of what this method ever sees is a colour somebody is half way through typing.
/// </summary>
public class ThemeColorsTests
{
    [Theory]
    [InlineData("#1E1E2E", 0x1E1E2Eu)]
    [InlineData("1E1E2E", 0x1E1E2Eu)]
    [InlineData("#1e1e2e", 0x1E1E2Eu)]
    [InlineData("  #1E1E2E  ", 0x1E1E2Eu)]
    [InlineData("#f0c", 0xFF00CCu)]
    public void RealColours_AreRead(string text, uint expected)
    {
        Assert.True(ThemeColors.TryParse(text, out var rgb));
        Assert.Equal(expected, rgb);
    }

    /// <summary>
    /// Half-typed text is the common case, not the exceptional one, and it is not an error worth
    /// reporting -- the box simply keeps showing the default until the sixth digit lands.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("#1e")]
    [InlineData("#1E1E2")]
    [InlineData("#1E1E2E1")]
    [InlineData("nonsense")]
    [InlineData("#GGGGGG")]
    public void AnythingElse_IsNotAColour(string? text)
    {
        Assert.False(ThemeColors.TryParse(text, out _));
    }

    /// <summary>Blank means default, in one place, so no caller has to remember it.</summary>
    [Fact]
    public void Resolve_FallsBackForAnythingUnreadable()
    {
        Assert.Equal(ThemeColors.DefaultSurface, ThemeColors.Resolve("", ThemeColors.DefaultSurface));
        Assert.Equal(ThemeColors.DefaultText, ThemeColors.Resolve("#1e", ThemeColors.DefaultText));
        Assert.Equal(0x1E1E2Eu, ThemeColors.Resolve("#1E1E2E", ThemeColors.DefaultSurface));
    }

    [Fact]
    public void ToHex_RoundTrips()
    {
        Assert.Equal("#1E1E2E", ThemeColors.ToHex(0x1E1E2E));
        Assert.True(ThemeColors.TryParse(ThemeColors.ToHex(0x0A0B0C), out var rgb));
        Assert.Equal(0x0A0B0Cu, rgb);
    }

    /// <summary>
    /// Weighted rather than averaged: the cheap average calls green and blue equally bright, which
    /// is why pure blue keeps coming out "light" under it and pure green does not.
    /// </summary>
    [Fact]
    public void Lightness_IsWeightedByChannel()
    {
        Assert.True(ThemeColors.IsLight(0xFFFFFF));
        Assert.True(ThemeColors.IsLight(0x00FF00));

        Assert.False(ThemeColors.IsLight(0x000000));
        Assert.False(ThemeColors.IsLight(0x0000FF));
        Assert.False(ThemeColors.IsLight(0x101010));
    }
}
