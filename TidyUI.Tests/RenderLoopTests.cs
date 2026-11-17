using System.Collections.Concurrent;
using TidyUI;

namespace TidyUI.Tests;

[Collection("Renderer")]
public sealed class RenderLoopTests
{
    [Theory]
    [InlineData(120, 40)]
    [InlineData(80, 24)]
    [InlineData(40, 12)]
    [InlineData(10, 5)]
    [InlineData(1, 1)]
    public async Task NestedLayoutWritesStayInsideSmallAndNormalTerminals(int width, int height)
    {
        UI.AutoResize(true);
        UI.UpdateInterval(2000);
        var terminal = new FakeTerminal(width, height);
        using var cts = new CancellationTokenSource();
        var layout = UI.SplitVertical(UI.Panel("Left", "some text"), UI.SplitHorizontal(UI.Panel("Top"), UI.Panel("Bottom")));

        var run = UI.RunAsync(layout, terminal, cts.Token);
        await terminal.WaitForAnyWriteAsync();
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(0, terminal.OutOfBoundsWrites);
        Assert.Equal(1, terminal.BeginCount);
        Assert.Equal(1, terminal.RestoreCount);
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public async Task ResizeRecalculatesLayoutAndRewritesNewFrame()
    {
        UI.AutoResize(true);
        UI.UpdateInterval(30);
        var terminal = new FakeTerminal(12, 8);
        using var cts = new CancellationTokenSource();
        var layout = UI.SplitVertical(UI.Panel("left"), UI.Panel("right"));
        var run = UI.RunAsync(layout, terminal, cts.Token);
        await terminal.WaitForCellAsync(10, 0, '┐');

        terminal.Resize(8, 4);
        UI.UpdateInterval(30); // wakes the single loop so it can observe the resize immediately
        await terminal.WaitForCellAsync(6, 0, '┐');
        Assert.Equal('┐', terminal.Read(6, 0));
        Assert.Equal(0, terminal.OutOfBoundsWrites);

        terminal.Resize(12, 8);
        UI.UpdateInterval(30);
        await terminal.WaitForCellAsync(10, 0, '┐');
        Assert.Equal(3, terminal.ClearCount);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ContentUpdatesRedrawOnlyChangedRuns()
    {
        UI.AutoResize(true);
        UI.UpdateInterval(100);
        var terminal = new FakeTerminal(20, 8);
        var panel = UI.Panel("panel", "first");
        using var cts = new CancellationTokenSource();
        var run = UI.RunAsync(panel, terminal, cts.Token);
        await terminal.WaitForCellAsync(2, 2, 'f');
        int writesAfterFirstFrame = terminal.WriteCallCount;
        await Task.Delay(250);
        Assert.Equal(writesAfterFirstFrame, terminal.WriteCallCount);

        panel.Content = "second";
        await terminal.WaitForCellAsync(2, 2, 's');
        Assert.True(terminal.WriteCallCount > writesAfterFirstFrame);
        Assert.Equal(0, terminal.OutOfBoundsWrites);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RuntimeIntervalChangesWakeOneRendererWithoutStartingDuplicates()
    {
        UI.AutoResize(true);
        UI.UpdateInterval(5000);
        var terminal = new FakeTerminal(16, 6);
        using var cts = new CancellationTokenSource();
        var layout = UI.Panel("watch");
        var run = UI.RunAsync(layout, terminal, cts.Token);
        await terminal.WaitForAnyWriteAsync();

        for (int interval = 25; interval <= 50; interval += 25)
        {
            int previousReads = terminal.DimensionReadCount;
            UI.UpdateInterval(interval);
            await terminal.WaitForDimensionReadsAsync(previousReads + 4);
        }

        var otherTerminal = new FakeTerminal(10, 4);
        await Assert.ThrowsAsync<InvalidOperationException>(() => UI.RunAsync(UI.Panel("second"), otherTerminal, cts.Token));
        Assert.Equal(0, otherTerminal.BeginCount);
        Assert.Equal(1, terminal.BeginCount);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, terminal.RestoreCount);
    }

    [Fact]
    public async Task OutputFailureIsReportedAndTerminalStateIsRestored()
    {
        UI.AutoResize(true);
        UI.UpdateInterval(500);
        var terminal = new FakeTerminal(10, 5) { FailWrites = true };
        var error = await Assert.ThrowsAsync<TerminalRenderException>(() => UI.RunAsync(UI.Panel("fail"), terminal, CancellationToken.None));

        Assert.Contains("could not render", error.Message);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(1, terminal.RestoreCount);
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public async Task DisablingResizeKeepsInitialLayoutButClipsToNewTerminalBounds()
    {
        UI.AutoResize(false);
        UI.UpdateInterval(5000);
        var terminal = new FakeTerminal(12, 6);
        var panel = UI.Panel("panel", "initial");
        using var cts = new CancellationTokenSource();
        var run = UI.RunAsync(panel, terminal, cts.Token);
        await terminal.WaitForCellAsync(10, 0, '┐');

        terminal.Resize(8, 4);
        panel.Content = "updated";
        UI.UpdateInterval(5000);
        await terminal.WaitForCellAsync(2, 2, 'u');
        Assert.Equal(0, terminal.OutOfBoundsWrites);

        UI.UpdateInterval(30);
        UI.AutoResize(true);
        await terminal.WaitForCellAsync(6, 0, '┐');
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task TerminalRestorationFailureIsReported()
    {
        UI.AutoResize(true);
        var terminal = new FakeTerminal(10, 5) { FailRestore = true };
        using var cts = new CancellationTokenSource();
        var run = UI.RunAsync(UI.Panel("restore"), terminal, cts.Token);
        await terminal.WaitForAnyWriteAsync();
        cts.Cancel();
        var error = await Assert.ThrowsAsync<TerminalRenderException>(() => run);
        Assert.Contains("restore the terminal state", error.Message);
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(1, terminal.RestoreCount);
    }

    [Fact]
    public async Task ResizeBetweenWritesAbandonsOldFrameAndRecoversWithFullRedraw()
    {
        UI.AutoResize(true);
        UI.UpdateInterval(30);
        var terminal = new FakeTerminal(20, 8);
        terminal.AfterWrite = (current, writeNumber) =>
        {
            if (writeNumber == 1) current.Resize(14, 5);
        };
        using var cts = new CancellationTokenSource();
        var run = UI.RunAsync(UI.Panel("resize recovery"), terminal, cts.Token);

        await terminal.WaitForCellAsync(12, 0, '┐');
        Assert.True(terminal.ClearCount >= 2, "The interrupted frame should be discarded and the new screen cleared before redraw.");
        Assert.Equal(0, terminal.OutOfBoundsWrites);
        Assert.Equal(0, terminal.RightmostColumnWrites);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RepeatedShrinksAreHandledAndFinalFrameUsesLatestSize()
    {
        UI.AutoResize(true);
        UI.UpdateInterval(20);
        var terminal = new FakeTerminal(30, 12);
        terminal.AfterWrite = (current, writeNumber) =>
        {
            switch (writeNumber)
            {
                case 1: current.Resize(26, 10); break;
                case 2: current.Resize(22, 8); break;
                case 3: current.Resize(18, 6); break;
                case 4: current.Resize(16, 5); break;
            }
        };
        using var cts = new CancellationTokenSource();
        var run = UI.RunAsync(UI.Panel("rapid"), terminal, cts.Token);

        await terminal.WaitForCellAsync(14, 0, '┐');
        Assert.True(terminal.ClearCount >= 5);
        Assert.Equal(0, terminal.OutOfBoundsWrites);
        Assert.Equal(0, terminal.RightmostColumnWrites);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RenderNeverWritesToRightmostColumnOnMultiColumnTerminal()
    {
        UI.AutoResize(true);
        UI.UpdateInterval(2000);
        var terminal = new FakeTerminal(10, 5);
        using var cts = new CancellationTokenSource();
        var run = UI.RunAsync(UI.Panel("margin"), terminal, cts.Token);
        await terminal.WaitForCellAsync(8, 0, '┐');

        Assert.Equal(0, terminal.RightmostColumnWrites);
        Assert.Equal('\0', terminal.Read(9, 0));
        Assert.Equal('\0', terminal.Read(9, 4));
        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, terminal.RestoreCount);
        Assert.True(terminal.CursorVisible);
    }

    [Fact]
    public async Task RapidAlternatingShrinksAndExpansionsRecoverAtLatestSize()
    {
        UI.AutoResize(true);
        UI.UpdateInterval(25);
        var terminal = new FakeTerminal(30, 12);
        var panel = UI.Panel("alternating");
        using var cts = new CancellationTokenSource();
        var run = UI.RunAsync(panel, terminal, cts.Token);
        await terminal.WaitForCellAsync(28, 0, '┐');
        int previousWriteCount = terminal.WriteCallCount;
        terminal.AfterWrite = (current, writeNumber) =>
        {
            switch (writeNumber - previousWriteCount)
            {
                case 1: current.Resize(26, 10); break;
                case 2: current.Resize(30, 12); break;
                case 3: current.Resize(20, 7); break;
                case 4: current.Resize(32, 13); break;
            }
        };
        panel.Content = "start a frame";

        await terminal.WaitForCellAsync(30, 0, '┐');
        Assert.True(terminal.ClearCount >= 5);
        Assert.Equal(0, terminal.OutOfBoundsWrites);
        Assert.Equal(0, terminal.RightmostColumnWrites);

        cts.Cancel();
        await run.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class FakeTerminal(int width, int height) : ITerminal
    {
        private readonly object _gate = new();
        private readonly Dictionary<(int X, int Y), char> _cells = new();
        private TaskCompletionSource _changed = NewSignal();
        private int _width = width;
        private int _height = height;
        private int _dimensionReadCount;
        private int _outOfBoundsWrites;
        private int _writeCallCount;
        private int _rightmostColumnWrites;
        private int _clearCount;

        public int Width { get { Interlocked.Increment(ref _dimensionReadCount); SignalChanged(); lock (_gate) return _width; } }
        public int Height { get { Interlocked.Increment(ref _dimensionReadCount); SignalChanged(); lock (_gate) return _height; } }
        public int DimensionReadCount => Volatile.Read(ref _dimensionReadCount);
        public int OutOfBoundsWrites => Volatile.Read(ref _outOfBoundsWrites);
        public int WriteCallCount => Volatile.Read(ref _writeCallCount);
        public int RightmostColumnWrites => Volatile.Read(ref _rightmostColumnWrites);
        public int ClearCount => Volatile.Read(ref _clearCount);
        public int BeginCount { get; private set; }
        public int RestoreCount { get; private set; }
        public bool CursorVisible { get; private set; } = true;
        public bool FailWrites { get; init; }
        public bool FailRestore { get; init; }
        public Action<FakeTerminal, int>? AfterWrite { get; set; }

        public void Begin() { BeginCount++; CursorVisible = false; }
        public void Restore()
        {
            RestoreCount++;
            if (FailRestore) throw new IOException("simulated terminal restore failure");
            CursorVisible = true;
        }
        public void Clear()
        {
            lock (_gate) _cells.Clear();
            Interlocked.Increment(ref _clearCount);
            SignalChanged();
        }
        public void Write(int x, int y, char value) => Write(x, y, value.ToString());

        public void Write(int x, int y, string value)
        {
            if (FailWrites) throw new IOException("simulated terminal output failure");
            int writeNumber;
            lock (_gate)
            {
                writeNumber = Interlocked.Increment(ref _writeCallCount);
                if (x < 0 || y < 0 || y >= _height || x + value.Length > _width)
                    Interlocked.Increment(ref _outOfBoundsWrites);
                if (value.Length > 0 && x + value.Length - 1 == _width - 1)
                    Interlocked.Increment(ref _rightmostColumnWrites);
                for (int i = 0; i < value.Length; i++) _cells[(x + i, y)] = value[i];
                var previous = _changed;
                _changed = NewSignal();
                previous.TrySetResult();
            }
            AfterWrite?.Invoke(this, writeNumber);
        }

        public void Resize(int newWidth, int newHeight) { lock (_gate) { _width = newWidth; _height = newHeight; } }
        public char Read(int x, int y) { lock (_gate) return _cells.TryGetValue((x, y), out var value) ? value : '\0'; }
        public (int Width, int Height) GetSize()
        {
            Interlocked.Add(ref _dimensionReadCount, 2);
            SignalChanged();
            lock (_gate) return (_width, _height);
        }

        public async Task WaitForAnyWriteAsync() => await WaitUntilAsync(() => _writeCallCount > 0);
        public async Task WaitForCellAsync(int x, int y, char value) => await WaitUntilAsync(() => Read(x, y) == value);
        public async Task WaitForDimensionReadsAsync(int count) => await WaitUntilAsync(() => DimensionReadCount >= count);

        private async Task WaitUntilAsync(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!condition())
            {
                Task changed;
                lock (_gate) changed = _changed.Task;
                try { await changed.WaitAsync(timeout.Token); }
                catch (OperationCanceledException) { throw new TimeoutException("The renderer did not produce the expected terminal state in time."); }
            }
        }

        private void SignalChanged()
        {
            lock (_gate)
            {
                var previous = _changed;
                _changed = NewSignal();
                previous.TrySetResult();
            }
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

[CollectionDefinition("Renderer", DisableParallelization = true)]
public sealed class RendererCollection { }
