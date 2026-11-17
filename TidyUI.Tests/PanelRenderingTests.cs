using TidyUI;

namespace TidyUI.Tests;

public sealed class PanelRenderingTests
{
    [Fact]
    public void RendersBorderTitleAndPaddedContentWithinAssignedRegion()
    {
        var panel = UI.Panel("Log", "Hi");
        var frame = panel.Render(new Region(0, 0, 10, 5));

        Assert.Equal('┌', frame[0, 0]);
        Assert.Equal('L', frame[0, 2]);
        Assert.Equal('┐', frame[0, 9]);
        Assert.Equal('H', frame[2, 2]);
        Assert.Equal('i', frame[2, 3]);
        Assert.Equal('└', frame[4, 0]);
        Assert.Equal('┘', frame[4, 9]);
    }

    [Fact]
    public void WrapsWordsToPanelInterior()
    {
        var frame = UI.Panel("", "alpha beta", padding: 0).Render(new Region(0, 0, 8, 5));
        Assert.Equal("alpha ", Row(frame, 1, 1, 6));
        Assert.Equal("beta  ", Row(frame, 2, 1, 6));
    }

    [Fact]
    public void TruncateModeUsesEllipsisForWidthAndHeightOverflow()
    {
        var wide = UI.Panel("", "abcdefgh", padding: 0, truncate: true).Render(new Region(0, 0, 8, 4));
        Assert.Equal("abcde…", Row(wide, 1, 1, 6));

        var tall = UI.Panel("", "one\ntwo\nthree", padding: 0, truncate: true).Render(new Region(0, 0, 8, 4));
        Assert.Equal("one   ", Row(tall, 1, 1, 6));
        Assert.Equal("two…  ", Row(tall, 2, 1, 6));
    }

    [Fact]
    public void TinyAndEmptyRegionsRenderWithoutEscapingTheirBuffer()
    {
        foreach (var size in new[] { (0, 0), (1, 1), (1, 8), (8, 1), (2, 2) })
        {
            var frame = UI.Panel("tiny", "text").Render(new Region(0, 0, size.Item1, size.Item2));
            Assert.Equal(size.Item2, frame.GetLength(0));
            Assert.Equal(size.Item1, frame.GetLength(1));
        }
    }

    private static string Row(char[,] frame, int y, int x, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = frame[y, x + i];
        return new string(chars);
    }
}
