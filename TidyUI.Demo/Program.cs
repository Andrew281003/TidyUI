using TidyUI;

UI.UpdateInterval(100);
UI.AutoResize(true);

var left = UI.Panel("Application Logs", "TidyUI is running. Resize the terminal to see the layout adapt.");
var right = UI.Panel("System Status", "Press Ctrl+C to exit.");
var layout = UI.SplitVertical(left, right, ratio: 0.5);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
UI.Run(layout, cancellation.Token);
