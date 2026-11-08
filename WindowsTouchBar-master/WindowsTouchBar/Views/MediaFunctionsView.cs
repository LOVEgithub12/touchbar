using SkiaSharp;
using WindowsInput.Native;
using WindowsTouchBar.Commands;
using WindowsTouchBar.Components;
using WindowsTouchBar.Interop;
using WindowsTouchBar.Models;
using WindowsTouchBar.Lyrics;

namespace WindowsTouchBar.Views;

internal class MediaFunctionsView : TouchBarView
{
    public override ViewType Type => ViewType.Media;
    private readonly Button previousButton;
    private readonly Button playButton;
    private readonly Button nextButton;
    private bool? controlsFoobar;

    public MediaFunctionsView(int width, int height) : base(width, height)
    {
        ComponentsGap = 20;
        Components.Add(new Spacer(true));
        var button = CreateButton(null, "\uE706", true);
        button.IconSize = 36;
        button.OnTouchDown += BrightlessDown;
        button = CreateButton(null, "\uE706", true);
        button.OnTouchDown += BrightlessUp;
        Components.Add(new Spacer(true));
        button = CreateButton(null, "\uED3A", true);
        button.OnTouchDown += (_, _) => AdjustKeyboardBacklight(-5);
        button = CreateButton(null, "\uED39", true);
        button.OnTouchDown += (_, _) => AdjustKeyboardBacklight(5);
        Components.Add(new Spacer(true));
        previousButton = CreateButton(VirtualKeyCode.MEDIA_PREV_TRACK, "\uE100");
        playButton = CreateButton(VirtualKeyCode.MEDIA_PLAY_PAUSE, "\uE102");
        nextButton = CreateButton(VirtualKeyCode.MEDIA_NEXT_TRACK, "\uE101");
        Components.Add(new Spacer(true));
        CreateButton(VirtualKeyCode.VOLUME_MUTE, "\uE74F");
        CreateButton(VirtualKeyCode.VOLUME_DOWN, "\uE993", true);
        CreateButton(VirtualKeyCode.VOLUME_UP, "\uE995", true);
        Components.Add(new Spacer(true));
        Update();
    }

    private MediaIconButton CreateButton(VirtualKeyCode? keyCode, string text, bool touchDownInterval = false, int width = 150)
    {
        var btnSize = SKRect.Create(width, Height - 4);
        var btn = new MediaIconButton(btnSize)
        {
            FillColor = new SKColor(38, 38, 40), DownFillColor = new SKColor(85, 85, 90), CornerRadius = new SKPoint(9, 9)
        };
        btn.Text = text;

        if (keyCode.HasValue)
        {
            btn.Command = new KeyPressCommand(keyCode);
        }

        btn.FontFamily = "Segoe MDL2 Assets";
        btn.FontSize = 36;
        btn.TouchDownInterval = touchDownInterval;
        Components.Add(btn);

        return btn;
    }

    public virtual void SetSnapshot(LyricsSnapshot snapshot)
    {
        playButton.Text = snapshot.IsPlaying ? "\uE769" : "\uE102";
        if (controlsFoobar == snapshot.Active) return;
        // Release the old target before changing commands; a generic media key
        // used to start foobar must receive KeyUp even if the finger is still down.
        previousButton.TouchUp(default);
        playButton.TouchUp(default);
        nextButton.TouchUp(default);
        controlsFoobar = snapshot.Active;
        previousButton.Command = snapshot.Active ? new FoobarPlaybackCommand("previous") : new KeyPressCommand(VirtualKeyCode.MEDIA_PREV_TRACK);
        playButton.Command = snapshot.Active ? new FoobarPlaybackCommand("playpause") : new KeyPressCommand(VirtualKeyCode.MEDIA_PLAY_PAUSE);
        nextButton.Command = snapshot.Active ? new FoobarPlaybackCommand("next") : new KeyPressCommand(VirtualKeyCode.MEDIA_NEXT_TRACK);
    }

    private void BrightlessUp(object? sender, EventArgs args)
    {
        AdjustScreenBrightness(2);
    }

    private void BrightlessDown(object? sender, EventArgs args)
    {
        AdjustScreenBrightness(-2);
    }

    internal static void AdjustScreenBrightness(int delta)
    {
        try
        {
            // Windows owns the slider, value updates, animation and auto-hide.
            // Notify before changing brightness so its watcher sees the change.
            NativeBrightnessOsd.Show();
            BrightlessAPI.Adjust(delta);
        }
        catch (Exception error)
        {
            ControlDiagnostics.Write($"Screen brightness: {error.Message}");
        }
    }

    internal static void AdjustKeyboardBacklight(int delta)
    {
        try { KeyboardBacklightAPI.Adjust(delta); }
        catch (Exception error)
        {
            ControlDiagnostics.Write($"Keyboard backlight: {error.Message}");
        }
    }
}
