using System.Runtime.ExceptionServices;

namespace TidyUI;

/// <summary>Reports a failure while TidyUI is rendering or restoring terminal state.</summary>
public sealed class TerminalRenderException : Exception
{
    /// <summary>Creates a terminal rendering exception.</summary>
    /// <param name="message">A description of the rendering or restoration failure.</param>
    /// <param name="innerException">The original exception that caused the failure.</param>
    public TerminalRenderException(string message, Exception innerException) : base(message, innerException) { }
}

internal sealed class RenderEngine
{
    private const int MaximumWriteRunLength = 32;
    private readonly ITerminal _terminal;
    private readonly Func<int> _getUpdateInterval;
    private readonly Func<bool> _getAutoResize;
    private readonly SemaphoreSlim _settingsChanged;
    private int _isRunning;

    internal RenderEngine(ITerminal terminal, Func<int> getUpdateInterval, Func<bool> getAutoResize, SemaphoreSlim settingsChanged)
    {
        _terminal = terminal;
        _getUpdateInterval = getUpdateInterval;
        _getAutoResize = getAutoResize;
        _settingsChanged = settingsChanged;
    }

    internal async Task RunAsync(LayoutNode layout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
            throw new InvalidOperationException("This TidyUI renderer is already running.");

        Exception? failure = null;
        bool beginAttempted = false;
        char[,]? previousFrame = null;
        int previousWidth = -1, previousHeight = -1;
        try
        {
            beginAttempted = true;
            _terminal.Begin();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int interval = _getUpdateInterval();
                if (interval <= 0) throw new InvalidOperationException("The terminal update interval must be greater than zero milliseconds.");
                try
                {
                    var measuredSize = _terminal.GetSize();
                    int terminalWidth = Math.Max(0, measuredSize.Width);
                    int terminalHeight = Math.Max(0, measuredSize.Height);
                    bool autoResize = _getAutoResize();
                    int width = Math.Max(0, autoResize || previousWidth < 0 ? terminalWidth : previousWidth);
                    int height = Math.Max(0, autoResize || previousHeight < 0 ? terminalHeight : previousHeight);
                    int frameWidth = GetSafeFrameWidth(width);
                    int writableWidth = Math.Min(frameWidth, GetSafeFrameWidth(terminalWidth));
                    int writableHeight = Math.Min(height, terminalHeight);

                    var frame = ComposeFrame(layout, frameWidth, height);
                    bool dimensionsChanged = width != previousWidth || height != previousHeight;
                    if (dimensionsChanged)
                    {
                        // Invalidate old cells as soon as a new size is observed.
                        if (_terminal.GetSize() != measuredSize) throw new TerminalFrameInterruptedException();
                        _terminal.Clear();
                        previousFrame = null;
                    }

                    WriteChanges(frame, previousFrame, dimensionsChanged, writableWidth, writableHeight, measuredSize);
                    if (_terminal.GetSize() != measuredSize) throw new TerminalFrameInterruptedException();

                    previousFrame = frame;
                    previousWidth = width;
                    previousHeight = height;
                }
                catch (TerminalFrameInterruptedException)
                {
                    // A frame is only useful as a diff base after every run completed at one size.
                    previousFrame = null;
                    previousWidth = -1;
                    previousHeight = -1;
                    await _settingsChanged.WaitAsync(TimeSpan.FromMilliseconds(interval), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _settingsChanged.WaitAsync(TimeSpan.FromMilliseconds(interval), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            failure = ex is TerminalRenderException
                ? ex
                : new TerminalRenderException("TidyUI could not render the terminal frame. Check that the output is an available interactive terminal and that the terminal has not changed size during output.", ex);
        }
        finally
        {
            if (beginAttempted)
            {
                try { _terminal.Restore(); }
                catch (Exception restoreError)
                {
                    failure = failure is null
                        ? new TerminalRenderException("TidyUI could not restore the terminal state after rendering.", restoreError)
                        : new TerminalRenderException("TidyUI rendering failed, and restoring terminal state also failed.", new AggregateException(failure, restoreError));
                }
            }
            Volatile.Write(ref _isRunning, 0);
        }

        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static char[,] ComposeFrame(LayoutNode layout, int width, int height)
    {
        var frame = new char[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) frame[y, x] = ' ';

        var placements = new List<(Panel Panel, Region Region)>();
        layout.Arrange(new Region(0, 0, width, height), placements);
        foreach (var (panel, assigned) in placements)
        {
            var region = Clip(assigned, width, height);
            if (region.IsEmpty) continue;
            var rendered = panel.Render(region);
            for (int y = 0; y < rendered.GetLength(0); y++)
                for (int x = 0; x < rendered.GetLength(1); x++)
                    frame[region.Y + y, region.X + x] = rendered[y, x];
        }
        return frame;
    }

    private static int GetSafeFrameWidth(int terminalWidth) => terminalWidth > 1 ? terminalWidth - 1 : terminalWidth;

    private static Region Clip(Region region, int width, int height)
    {
        int x = Math.Clamp(region.X, 0, width);
        int y = Math.Clamp(region.Y, 0, height);
        int right = (int)Math.Clamp((long)region.X + Math.Max(0, region.Width), x, width);
        int bottom = (int)Math.Clamp((long)region.Y + Math.Max(0, region.Height), y, height);
        return new Region(x, y, right - x, bottom - y);
    }

    private void WriteChanges(char[,] frame, char[,]? previous, bool dimensionsChanged, int terminalWidth, int terminalHeight, (int Width, int Height) expectedSize)
    {
        int height = Math.Min(frame.GetLength(0), terminalHeight);
        int width = Math.Min(frame.GetLength(1), terminalWidth);
        for (int y = 0; y < height; y++)
        {
            int x = 0;
            while (x < width)
            {
                while (x < width && !IsChanged(frame, previous, dimensionsChanged, x, y)) x++;
                if (x == width) break;
                int start = x;
                while (x < width && IsChanged(frame, previous, dimensionsChanged, x, y)) x++;
                int runEnd = x;
                while (start < runEnd)
                {
                    int length = Math.Min(MaximumWriteRunLength, runEnd - start);
                    var run = new char[length];
                    for (int i = 0; i < run.Length; i++) run[i] = frame[y, start + i];
                    if (_terminal.GetSize() != expectedSize) throw new TerminalFrameInterruptedException();
                    _terminal.Write(start, y, new string(run));
                    if (_terminal.GetSize() != expectedSize) throw new TerminalFrameInterruptedException();
                    start += length;
                }
            }
        }
    }

    private static bool IsChanged(char[,] frame, char[,]? previous, bool dimensionsChanged, int x, int y)
    {
        return dimensionsChanged || previous is null ||
               y >= previous.GetLength(0) || x >= previous.GetLength(1) ||
               frame[y, x] != previous[y, x];
    }
}
