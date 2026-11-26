using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;
using WindowsTouchBar.Commands;
using WindowsTouchBar.Components;
using WindowsTouchBar.Events;
using WindowsTouchBar.Models;
using WindowsTouchBar.Views;
using WindowsInput.Native;

internal static class ChromeButtonChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "settings.json")), options)!;
        var chrome = settings.Views!.Single(view => view.Application == "chrome.exe");
        var edge = settings.Views!.Single(view => view.Application == "msedge.exe");

        foreach (int width in new[] { 2008, 2170 })
        {
            var view = new CustomFunctionsView(width, 60);
            view.ConfigureView(chrome);
            var buttons = view.Components.OfType<Button>().ToArray();
            var entry = buttons[3];
            Require(buttons.Length == 9 && entry.Command is null,
                "The new icon removed a shortcut or acquired a keyboard command.");
            Require(entry.X == 390 && entry.Y == 2 && entry.Width == 112 && entry.Height == 56 &&
                entry.X - (buttons[2].X + buttons[2].Width) == 10,
                "The Chrome icon does not immediately follow Refresh at the selected dimensions.");
            Require(entry.FillColor == new SKColor(65, 65, 65), "The icon background differs from browser buttons.");
            Require(buttons[2].Command is KeyPressCommand refresh &&
                refresh.KeyCodes is { Length: 1 } && refresh.KeyCodes[0] == VirtualKeyCode.F5,
                "Refresh no longer sends F5.");
            Require(buttons.All(button => button.X >= 0 && button.Y >= 0 &&
                button.X + button.Width <= width && button.Y + button.Height <= 60),
                "Adding Chrome pushed an existing shortcut outside the panel.");

            using (var bitmap = new SKBitmap(width, 60, SKColorType.Rgba8888, SKAlphaType.Opaque))
            using (var canvas = new SKCanvas(bitmap))
            {
                view.Clear(canvas); view.Draw(canvas);
                int colored = 0;
                for (int y = 8; y < 52; y++)
                    for (int x = 422; x < 470; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        if (Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)) -
                            Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue)) > 60) colored++;
                    }
                Require(colored > 600, "The installed Chrome logo is absent or too small to see.");
            }

            int requests = 0;
            view.MainRequested += () => requests++;
            view.ReceiveTouch(new TouchEventArgs(446, 30, TouchEventType.TouchUp));
            view.ReceiveTouch(new TouchEventArgs(446, 30, TouchEventType.TouchMove));
            Require(requests == 0, "An orphan release or slide-in opened main.");
            view.ReceiveTouch(new TouchEventArgs(446, 30, TouchEventType.TouchDown));
            Require(requests == 0, "Main opened before the icon was released.");
            view.ReceiveTouch(new TouchEventArgs(446, 30, TouchEventType.TouchUp));
            Require(requests == 1, "A complete Chrome tap did not request main exactly once.");

            // Use a fake command: reconfiguration must release a held shortcut,
            // and a stale disposed entry must not emit navigation later.
            var probe = new ProbeCommand();
            buttons[2].Command = probe;
            view.ReceiveTouch(new TouchEventArgs(320, 30, TouchEventType.TouchDown));
            Require(probe.Down == 1 && probe.Up == 0, "The existing Refresh touch stopped working.");
            view.ConfigureView(edge);
            Require(probe.Up == 1, "Reconfiguration left the previous shortcut held.");
            Require(view.Components.OfType<Button>().Count() == 8 &&
                view.Components.OfType<Button>().All(button => button.Command is not null),
                "The Chrome logo leaked into another application's layout.");
            entry.TouchDown(default); entry.TouchUp(default);
            Require(requests == 1, "A disposed old Chrome entry emitted navigation.");
            foreach (var button in view.Components.OfType<Button>()) button.Dispose();
        }
    }

    private sealed class ProbeCommand : ITouchCommand
    {
        public int Down, Up;
        public void TouchDown() => Down++;
        public void TouchUp() => Up++;
    }
}
