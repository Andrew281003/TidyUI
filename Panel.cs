namespace TidyUI;

/// <summary>A bordered text panel rendered inside its assigned region.</summary>
public sealed class Panel : LayoutNode
{
    /// <summary>Creates a panel with optional initial text and padding.</summary>
    /// <param name="title">The text shown in the panel's top border.</param>
    /// <param name="content">Initial panel content. Text wraps by default.</param>
    /// <param name="padding">The number of blank cells around content.</param>
    /// <param name="truncate"><see langword="true"/> to clip overflowing content with an ellipsis instead of wrapping it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="padding"/> is negative.</exception>
    public Panel(string title, string content = "", int padding = 1, bool truncate = false)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(content);
        if (padding < 0) throw new ArgumentOutOfRangeException(nameof(padding), "Padding cannot be negative.");
        Title = title; Content = content; Padding = padding; Truncate = truncate;
    }

    /// <summary>Gets the panel title.</summary>
    public string Title { get; }
    /// <summary>Gets or sets panel text content.</summary>
    public string Content { get; set; }
    /// <summary>Gets the number of blank cells around content.</summary>
    public int Padding { get; }
    /// <summary>Gets whether content that does not fit is clipped with an ellipsis instead of wrapped.</summary>
    public bool Truncate { get; }

    internal override void Arrange(Region region, List<(Panel Panel, Region Region)> output) => output.Add((this, region));

    internal char[,] Render(Region region)
    {
        var buffer = new char[Math.Max(0, region.Height), Math.Max(0, region.Width)];
        for (int y=0;y<buffer.GetLength(0);y++) for(int x=0;x<buffer.GetLength(1);x++) buffer[y,x]=' ';
        int h=buffer.GetLength(0), w=buffer.GetLength(1);
        if(h==0||w==0) return buffer;
        if(w==1||h==1) { for(int y=0;y<h;y++) for(int x=0;x<w;x++) buffer[y,x]='·'; return buffer; }
        for(int x=1;x<w-1;x++) { buffer[0,x]='─'; buffer[h-1,x]='─'; }
        for(int y=1;y<h-1;y++) { buffer[y,0]='│'; buffer[y,w-1]='│'; }
        buffer[0,0]='┌'; buffer[0,w-1]='┐'; buffer[h-1,0]='└'; buffer[h-1,w-1]='┘';
        int titleLength=Math.Min(Title.Length, Math.Max(0,w-4));
        for(int i=0;i<titleLength;i++) buffer[0,i+2]=Title[i];
        int left=Math.Min(Padding+1,w-1), right=Math.Max(left,w-Padding-1), top=Math.Min(Padding+1,h-1);
        int innerWidth=Math.Max(0,right-left);
        int row=top;
        string[] lines = Content.Replace("\r", "").Split('\n');
        if (Truncate)
        {
            int lastContentRow = h - 1;
            int visibleLines = Math.Max(0, lastContentRow - row);
            for (int i = 0; i < Math.Min(visibleLines, lines.Length); i++)
            {
                string line = lines[i];
                bool horizontalOverflow = line.Length > innerWidth;
                bool verticalOverflow = i == visibleLines - 1 && lines.Length > visibleLines;
                if ((horizontalOverflow || verticalOverflow) && innerWidth > 0)
                {
                    int copyLength = Math.Min(Math.Max(0, innerWidth - 1), line.Length);
                    for (int j = 0; j < copyLength; j++) buffer[row, left + j] = line[j];
                    buffer[row, left + copyLength] = '…';
                }
                else
                {
                    for (int j = 0; j < Math.Min(innerWidth, line.Length); j++) buffer[row, left + j] = line[j];
                }
                row++;
            }
            return buffer;
        }

        foreach (var line in lines)
        foreach (var wrapped in Wrap(line, innerWidth))
        {
            if (row >= h - 1) return buffer;
            for (int i = 0; i < Math.Min(innerWidth, wrapped.Length); i++) buffer[row, left + i] = wrapped[i];
            row++;
        }
        return buffer;
    }

    private static IEnumerable<string> Wrap(string text,int width)
    {
        if(width<=0) yield break;
        if(text.Length==0) { yield return ""; yield break; }
        int start=0;
        while(start<text.Length)
        {
            int take=Math.Min(width,text.Length-start);
            if(start+take<text.Length) { int space=text.LastIndexOf(' ',start+take-1,take); if(space>=start) take=space-start==0?take:space-start; }
            yield return text.Substring(start,take).TrimEnd();
            start+=take; while(start<text.Length&&text[start]==' ') start++;
        }
    }
}
