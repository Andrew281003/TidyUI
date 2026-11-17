using TidyUI;

namespace TidyUI.Tests;

public sealed class LayoutTests
{
    [Theory]
    [InlineData(120, 40)]
    [InlineData(80, 24)]
    [InlineData(40, 12)]
    [InlineData(10, 5)]
    [InlineData(1, 1)]
    public void NestedSplitRegionsStayWithinTerminal(int width, int height)
    {
        var root = UI.SplitVertical(UI.Panel("A"), UI.SplitHorizontal(UI.Panel("B"), UI.Panel("C"), .4), .5);
        var placements = new List<(Panel Panel, Region Region)>();
        root.Arrange(new Region(0, 0, width, height), placements);
        Assert.Equal(3, placements.Count);
        foreach (var (_, region) in placements)
        {
            Assert.True(region.X >= 0 && region.Y >= 0 && region.Width >= 0 && region.Height >= 0);
            Assert.True((long)region.X + region.Width <= width);
            Assert.True((long)region.Y + region.Height <= height);
        }
    }

    [Fact]
    public void SplitRatiosRoundAndUseAllCells()
    {
        var root = UI.SplitVertical(UI.Panel("A"), UI.Panel("B"), .5);
        var placements = new List<(Panel Panel, Region Region)>();
        root.Arrange(new Region(0, 0, 5, 2), placements);
        Assert.Equal(3, placements[0].Region.Width);
        Assert.Equal(2, placements[1].Region.Width);
        Assert.Equal(5, placements.Sum(x => x.Region.Width));
    }

    [Fact]
    public void InvalidArgumentsHaveUsefulMessages()
    {
        Assert.Contains("greater than zero", Assert.Throws<ArgumentOutOfRangeException>(() => UI.UpdateInterval(0)).Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => UI.SplitVertical(UI.Panel("A"), UI.Panel("B"), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => UI.Panel("A", padding: -1));
    }
}
