namespace TidyUI;

/// <summary>A rectangular area in terminal cells.</summary>
/// <param name="X">The zero-based horizontal position.</param>
/// <param name="Y">The zero-based vertical position.</param>
/// <param name="Width">The number of columns.</param>
/// <param name="Height">The number of rows.</param>
public readonly record struct Region(int X, int Y, int Width, int Height)
{
    /// <summary>Gets whether the region has no renderable cells.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>Provides terminal size and cell output operations.</summary>
public interface ITerminal
{
    /// <summary>Gets the current terminal width in cells.</summary>
    int Width { get; }
    /// <summary>Gets the current terminal height in cells.</summary>
    int Height { get; }
    /// <summary>Gets a terminal width and height snapshot, in cells.</summary>
    (int Width, int Height) GetSize() => (Width, Height);
    /// <summary>Writes a character at an absolute terminal cell.</summary>
    void Write(int x, int y, char value);
    /// <summary>Writes a run of characters at an absolute terminal cell.</summary>
    void Write(int x, int y, string value)
    {
        for (var i = 0; i < value.Length; i++) Write(x + i, y, value[i]);
    }
    /// <summary>Clears the terminal.</summary>
    void Clear();
    /// <summary>Begins a render session and saves any terminal state that will be changed.</summary>
    void Begin() { }
    /// <summary>Restores terminal state changed by <see cref="Begin"/>.</summary>
    void Restore() { }
}

internal sealed class ConsoleTerminal : ITerminal
{
    private bool _cursorStateCaptured;
    private bool _previousCursorVisible;
    private bool _cursorStateChanged;
    private bool _cursorPositionCaptured;
    private int _previousCursorLeft;
    private int _previousCursorTop;

    public int Width => ReadDimension(() => Console.WindowWidth, "width");
    public int Height => ReadDimension(() => Console.WindowHeight, "height");
    public (int Width, int Height) GetSize() => (Width, Height);
    public void Write(int x, int y, char value) => Write(x, y, value.ToString());
    public void Write(int x, int y, string value)
    {
        var size = GetSize();
        if (x < 0 || y < 0 || x + value.Length > size.Width || y >= size.Height)
            throw new TerminalFrameInterruptedException();

        try
        {
            Console.SetCursorPosition(x, y);
            Console.Write(value);
        }
        catch (Exception ex) when (ex is IOException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            var current = GetSize();
            if (current != size || x + value.Length > current.Width || y >= current.Height)
                throw new TerminalFrameInterruptedException();
            throw;
        }
    }
    public void Clear() => Console.Clear();

#pragma warning disable CA1416 // Cursor state operations are best-effort and guarded for unsupported terminals.
    public void Begin()
    {
        try
        {
            _previousCursorLeft = Console.CursorLeft;
            _previousCursorTop = Console.CursorTop;
            _cursorPositionCaptured = true;
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        catch (PlatformNotSupportedException) { }

        try
        {
            _previousCursorVisible = Console.CursorVisible;
            _cursorStateCaptured = true;
            _cursorStateChanged = true;
            Console.CursorVisible = false;
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        catch (PlatformNotSupportedException) { }
    }

    public void Restore()
    {
        Exception? failure = null;
        if (_cursorPositionCaptured)
        {
            try
            {
                int width = Width, height = Height;
                if (width > 0 && height > 0)
                    Console.SetCursorPosition(Math.Clamp(_previousCursorLeft, 0, width - 1), Math.Clamp(_previousCursorTop, 0, height - 1));
            }
            catch (Exception ex) { failure = ex; }
        }
        if (_cursorStateCaptured && _cursorStateChanged)
        {
            try
            {
                Console.CursorVisible = _previousCursorVisible;
                _cursorStateChanged = false;
            }
            catch (Exception ex) { failure = failure is null ? ex : new AggregateException(failure, ex); }
        }
        if (failure is not null) throw new IOException("One or more terminal settings could not be restored.", failure);
    }
#pragma warning restore CA1416

    private static int ReadDimension(Func<int> read, string dimension)
    {
        if (Console.IsOutputRedirected)
            throw new InvalidOperationException("TidyUI requires an interactive terminal; standard output is redirected.");
        try { return Math.Max(0, read()); }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"TidyUI could not read the terminal {dimension}.", ex);
        }
    }
}

internal sealed class TerminalFrameInterruptedException : Exception { }
