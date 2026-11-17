namespace TidyUI;

/// <summary>Base node in a fixed-at-startup layout tree.</summary>
public abstract class LayoutNode { internal abstract void Arrange(Region region, List<(Panel Panel, Region Region)> output); }

/// <summary>Splits the assigned region between two child nodes.</summary>
public sealed class Split : LayoutNode
{
    internal LayoutNode First { get; }
    internal LayoutNode Second { get; }
    internal bool IsVertical { get; }
    internal double Ratio { get; }

    internal Split(LayoutNode first, LayoutNode second, bool isVertical, double ratio)
    { First = first; Second = second; IsVertical = isVertical; Ratio = ratio; }

    internal override void Arrange(Region region, List<(Panel Panel, Region Region)> output)
    {
        int total = IsVertical ? region.Width : region.Height;
        int firstSize = (int)Math.Round(total * Ratio, MidpointRounding.AwayFromZero);
        firstSize = Math.Clamp(firstSize, 0, total);
        int secondSize = total - firstSize;
        var a = IsVertical ? new Region(region.X, region.Y, firstSize, region.Height) : new Region(region.X, region.Y, region.Width, firstSize);
        var b = IsVertical ? new Region(region.X + firstSize, region.Y, secondSize, region.Height) : new Region(region.X, region.Y + firstSize, region.Width, secondSize);
        First.Arrange(a, output); Second.Arrange(b, output);
    }
}
