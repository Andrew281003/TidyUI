namespace TidyUI;

/// <summary>Creates layouts and runs the centralized terminal renderer.</summary>
public static class UI
{
    private static int _updateInterval = 100;
    private static bool _autoResize = true;
    private static readonly SemaphoreSlim SettingsChanged = new(0, 1);
    private static int _isRunning;

    /// <summary>Sets the terminal size polling interval in milliseconds. The default is 100 milliseconds.</summary>
    /// <param name="milliseconds">A positive number of milliseconds between terminal checks.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public static void UpdateInterval(int milliseconds)
    {
        if (milliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(milliseconds), "Update interval must be greater than zero milliseconds.");
        Volatile.Write(ref _updateInterval, milliseconds);
        SignalSettingsChanged();
    }
    /// <summary>Enables or disables automatic terminal resize monitoring. Monitoring is enabled by default.</summary>
    /// <param name="enabled"><see langword="true"/> to recalculate layout after a resize; otherwise, <see langword="false"/>.</param>
    public static void AutoResize(bool enabled)
    {
        Volatile.Write(ref _autoResize, enabled);
        SignalSettingsChanged();
    }
    /// <summary>Creates a bordered text panel.</summary>
    /// <param name="title">The text shown in the panel's top border.</param>
    /// <param name="content">Initial panel content. Text wraps by default.</param>
    /// <param name="padding">The number of blank cells around content.</param>
    /// <param name="truncate"><see langword="true"/> to clip overflowing content with an ellipsis instead of wrapping it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> or <paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="padding"/> is negative.</exception>
    public static Panel Panel(string title,string content="",int padding=1, bool truncate=false) => new(title,content,padding,truncate);
    /// <summary>Splits the layout into left and right regions with a vertical dividing line.</summary>
    /// <param name="left">The layout node for the left region.</param>
    /// <param name="right">The layout node for the right region.</param>
    /// <param name="ratio">The share of available width assigned to the left region. Must be greater than zero and less than one.</param>
    /// <exception cref="ArgumentNullException">Either layout node is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ratio"/> is not between zero and one.</exception>
    public static LayoutNode SplitVertical(LayoutNode left,LayoutNode right,double ratio=0.5) => CreateSplit(left,right,true,ratio);
    /// <summary>Splits the layout into top and bottom regions with a horizontal dividing line.</summary>
    /// <param name="top">The layout node for the top region.</param>
    /// <param name="bottom">The layout node for the bottom region.</param>
    /// <param name="ratio">The share of available height assigned to the top region. Must be greater than zero and less than one.</param>
    /// <exception cref="ArgumentNullException">Either layout node is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ratio"/> is not between zero and one.</exception>
    public static LayoutNode SplitHorizontal(LayoutNode top,LayoutNode bottom,double ratio=0.5) => CreateSplit(top,bottom,false,ratio);
    private static LayoutNode CreateSplit(LayoutNode first,LayoutNode second,bool vertical,double ratio)
    { ArgumentNullException.ThrowIfNull(first); ArgumentNullException.ThrowIfNull(second); if(double.IsNaN(ratio)||ratio<=0||ratio>=1) throw new ArgumentOutOfRangeException(nameof(ratio),"Split ratio must be greater than zero and less than one."); return new Split(first,second,vertical,ratio); }
    /// <summary>Renders the layout until cancellation. Terminal rendering failures are reported as <see cref="TerminalRenderException"/>.</summary>
    /// <param name="layout">The layout tree to render.</param>
    /// <param name="cancellationToken">A token used to stop rendering and restore terminal state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Another TidyUI renderer is already running.</exception>
    /// <exception cref="TerminalRenderException">Terminal output or restoration failed.</exception>
    public static void Run(LayoutNode layout,CancellationToken cancellationToken=default) => RunAsync(layout,cancellationToken).GetAwaiter().GetResult();
    /// <summary>Asynchronously renders the layout until cancellation. Terminal rendering failures are reported as <see cref="TerminalRenderException"/>.</summary>
    /// <param name="layout">The layout tree to render.</param>
    /// <param name="cancellationToken">A token used to stop rendering and restore terminal state.</param>
    /// <returns>A task that completes when rendering is canceled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Another TidyUI renderer is already running.</exception>
    /// <exception cref="TerminalRenderException">Terminal output or restoration failed.</exception>
    public static async Task RunAsync(LayoutNode layout,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        await RunAsync(layout, new ConsoleTerminal(), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task RunAsync(LayoutNode layout, ITerminal terminal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(terminal);
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            throw new InvalidOperationException("A TidyUI renderer is already running. Cancel it before starting another renderer.");
        try
        {
            var engine = new RenderEngine(terminal,
                () => Volatile.Read(ref _updateInterval),
                () => Volatile.Read(ref _autoResize), SettingsChanged);
            await engine.RunAsync(layout, cancellationToken).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref _isRunning, 0); }
    }

    private static void SignalSettingsChanged()
    {
        try { SettingsChanged.Release(); }
        catch (SemaphoreFullException) { }
    }
}
