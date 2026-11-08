using System.Globalization;
using System.Diagnostics;
using SkiaSharp;
using WindowsInput.Native;
using WindowsTouchBar.Commands;
using WindowsTouchBar.Components;
using WindowsTouchBar.Lyrics;
using WindowsTouchBar.Models;
using WindowsTouchBar.Helpers;

namespace WindowsTouchBar.Views;

internal sealed class LyricsView : TouchBarView
{
    public override ViewType Type => ViewType.Media;
    private readonly LyricsLabel label;
    private readonly Button playButton;
    private readonly AuroraRibbonRenderer aurora = new();
    private long backgroundFrame;
    public event Action? MainRequested;
    public event Action? VisualizerToggleRequested;
    public bool VisualizerEnabled
    {
        get => label.VisualizerEnabled;
        set
        {
            label.VisualizerEnabled = value;
            if (!value) aurora.Reset();
            foreach (var button in Components.OfType<Button>().Where(button => !ReferenceEquals(button, label)))
                button.FillColor = value ? new SKColor(28, 28, 33, 135) : new SKColor(38, 38, 40);
        }
    }
    public bool NeedsAnimation => label.NeedsAnimation;
    public void SetAudioEnergy(float bass, float mid, float treble, float level) => aurora.SetEnergy(bass, mid, treble, level);
    public void ResetAnimation() { label.ResetAnimation(); backgroundFrame = 0; aurora.Reset(); }

    public override void Draw(SKCanvas canvas)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsed = backgroundFrame == 0 ? 1.0 / 60 : (now - backgroundFrame) / (double)Stopwatch.Frequency;
        backgroundFrame = now;
        if (VisualizerEnabled && label.Snapshot.ShowLyrics)
        {
            // Keep the animated background inside the lyric surface. The side
            // controls remain stable and readable while the lyric area retains
            // its own local coordinate system.
            canvas.Save();
            canvas.Translate(label.X, label.Y);
            aurora.Draw(canvas, label.Width, label.Height, elapsed);
            canvas.Restore();
        }
        label.HasAudioEnergy = aurora.HasEnergy;
        base.Draw(canvas);
    }

    public LyricsView(int width, int height) : base(width, height)
    {
        ComponentsGap = 20;
        Components.Add(new Spacer(12));
        AddControl("\uE100", 144).Command = new FoobarPlaybackCommand("previous");
        playButton = AddControl("\uE769", 144);
        playButton.Command = new FoobarPlaybackCommand("playpause");
        AddControl("\uE101", 144).Command = new FoobarPlaybackCommand("next");
        var player = new PlayerIconButton(SKRect.Create(112, height - 4));
        player.OnTouchUp += (_, _) => MainRequested?.Invoke();
        Components.Add(player);
        Components.Add(new Spacer(24));
        // Keep x=640 and extend right to 20px before the existing volume group.
        label = new LyricsLabel(SKRect.Create(width - 1080, height - 4));
        label.OnTouchUp += (_, _) => VisualizerToggleRequested?.Invoke();
        Components.Add(label);
        Components.Add(new Spacer(true));
        AddControl("\uE74F", 112).Command = new KeyPressCommand(VirtualKeyCode.VOLUME_MUTE);
        AddControl("\uE993", 128, true).Command = new KeyPressCommand(VirtualKeyCode.VOLUME_DOWN);
        AddControl("\uE995", 128, true).Command = new KeyPressCommand(VirtualKeyCode.VOLUME_UP);
        Components.Add(new Spacer(12));
        Update();
        VisualizerEnabled = true;
    }

    private Button AddControl(string icon, int width, bool repeat = false)
    {
        var button = new MediaIconButton(SKRect.Create(width, Height - 4))
        {
            Text = icon, FontFamily = "Segoe MDL2 Assets", FontSize = 36,
            FillColor = new SKColor(38, 38, 40), DownFillColor = new SKColor(85, 85, 90),
            CornerRadius = new SKPoint(9, 9), TouchDownInterval = repeat
        };
        Components.Add(button);
        return button;
    }

    public void SetSnapshot(LyricsSnapshot snapshot)
    {
        label.Snapshot = snapshot;
        playButton.Text = snapshot.IsPaused ? "\uE102" : "\uE769";
    }

    private sealed class PlayerIconButton : Button
    {
        private readonly SKBitmap? icon;
        public PlayerIconButton(SKRect size) : base(size)
        {
            FillColor = new SKColor(38, 38, 40);
            DownFillColor = new SKColor(85, 85, 90);
            CornerRadius = new SKPoint(9, 9);
            string path = Path.Combine(AppContext.BaseDirectory, "Resources", "foobar2000.png");
            if (File.Exists(path)) icon = SKBitmap.Decode(path);
            if (icon is null) { Text = "\uE8D6"; FontFamily = "Segoe MDL2 Assets"; FontSize = 36; }
        }
        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
            if (icon is null) return;
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            const float iconSize = 43.2f;
            canvas.DrawBitmap(icon, SKRect.Create(X + (Width - iconSize) / 2, Y + (Height - iconSize) / 2, iconSize, iconSize), paint);
        }
    }

    private sealed class LyricsLabel : Button
    {
        private readonly LyricsFrameAnimator animation = new();
        private readonly LyricsViewport viewport = new();
        private LyricsSnapshot snapshot = LyricsSnapshot.Inactive;
        private string renderedText = "";
        private string renderedTrack = "";
        private double renderedPosition;
        private long changedAt;
        private double linePosition;
        private bool overflow;
        private long lastFrame;
        private bool resetViewport = true;
        public bool VisualizerEnabled { get; set; } = true;
        public bool HasAudioEnergy { get; set; }
        public void ResetAnimation() { resetViewport = true; lastFrame = 0; }
        private static double Elapsed(long ticks) => ticks == 0 ? double.MaxValue : (Stopwatch.GetTimestamp() - ticks) / (double)Stopwatch.Frequency;
        public bool NeedsAnimation => Elapsed(changedAt) < 0.12 ||
            (snapshot.IsPlaying && (overflow || snapshot.HasLyrics || VisualizerEnabled));
        public LyricsSnapshot Snapshot
        {
            get => snapshot;
            set
            {
                animation.Update(value);
                snapshot = value;
            }
        }
        private static string MainText(LyricsSnapshot value) =>
            (string.IsNullOrWhiteSpace(value.CurrentLine) ? value.Title : value.CurrentLine).Replace('\r', ' ').Replace('\n', ' ');
        public LyricsLabel(SKRect size) : base(size) { FillColor = SKColors.Black; }

        public override void Draw(SKCanvas canvas)
        {
            var frame = animation.Current;
            string text = MainText(frame);
            string track = string.IsNullOrEmpty(frame.TrackKey) ? frame.Title + "\0" + frame.Artist : frame.TrackKey;
            long now = Stopwatch.GetTimestamp();
            double elapsed = lastFrame == 0 ? 1.0 / 60 : Math.Min(0.1, (now - lastFrame) / (double)Stopwatch.Frequency);
            lastFrame = now;
            if (text != renderedText || track != renderedTrack)
            {
                resetViewport = true;
                changedAt = renderedText.Length > 0 ? Stopwatch.GetTimestamp() : 0;
                linePosition = frame.PositionSeconds;
                renderedText = text;
                renderedTrack = track;
            }
            else if (Math.Abs(frame.PositionSeconds - renderedPosition) > 1.5)
            {
                linePosition = frame.PositionSeconds;
                resetViewport = true;
            }
            renderedPosition = frame.PositionSeconds;
            canvas.Save();
            canvas.Translate(X, Y);
            canvas.ClipRect(SKRect.Create(Width, Height));
            using var layout = new LyricsTextLayout(text);
            using var paint = new SKPaint
            {
                IsAntialias = true, TextAlign = SKTextAlign.Left, TextSize = 34,
                HintingLevel = SKPaintHinting.Full, SubpixelText = false, LcdRenderText = false
            };
            while (paint.TextSize > 28 && layout.MeasureText(paint) > Width - 30) paint.TextSize--;
            float textWidth = layout.MeasureText(paint);
            overflow = textWidth > Width - 30;
            var bounds = layout.MeasureBounds(paint);
            float baseline = MathF.Round((Height - bounds.Height) / 2 - bounds.Top);
            float x = MathF.Round((Width - textWidth) / 2);
            bool timed = frame.HasLyrics && !string.IsNullOrWhiteSpace(frame.CurrentLine);
            float sungWidth = timed ? HighlightWidth(text, Math.Clamp(frame.Progress, 0, 1), paint, layout) : 0;
            if (overflow && timed)
            {
                x = 15 + viewport.GetX(textWidth, Width - 30, sungWidth, elapsed, resetViewport);
            }
            else if (overflow)
            {
                double sinceLine = Math.Max(0, frame.PositionSeconds - linePosition);
                double distance = textWidth - Width + 30;
                double travel = Math.Max(1, distance / 45);
                double cycle = sinceLine % (2 * travel + 2.4);
                double fraction = cycle <= 1.2 ? 0 : cycle <= travel + 1.2 ? (cycle - 1.2) / travel :
                    cycle <= travel + 2.4 ? 1 : 1 - (cycle - travel - 2.4) / travel;
                x = 15 - (float)(distance * fraction);
            }
            resetViewport = false;
            // Edge fades apply only to glyphs; the ribbons stay continuous.
            if (overflow) canvas.SaveLayer(SKRect.Create(Width, Height), null);
            const byte alpha = 255;
            if (VisualizerEnabled && HasAudioEnergy)
            {
                using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 210),
                    Style = SKPaintStyle.Stroke, StrokeWidth = 2.2f, TextSize = paint.TextSize };
                layout.Draw(canvas, x, baseline, shadow);
            }
            paint.Color = SKColors.White.WithAlpha(alpha);
            layout.Draw(canvas, x, baseline, paint);
            if (timed)
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(x, 0, x + sungWidth, Height));
                using var gradient = SKShader.CreateLinearGradient(new SKPoint(x, 0), new SKPoint(x + Math.Max(1, textWidth), 0),
                    new[] { new SKColor(174, 153, 255), new SKColor(118, 201, 255) }, null, SKShaderTileMode.Clamp);
                paint.Color = SKColors.White.WithAlpha(alpha);
                paint.Shader = gradient;
                layout.Draw(canvas, x, baseline, paint);
                paint.Shader = null;
                canvas.Restore();
            }
            if (overflow)
            {
                using var fade = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(Width, 0),
                    new[] { SKColors.Transparent, SKColors.White, SKColors.White, SKColors.Transparent },
                    new[] { 0f, 15 / Width, 1 - 15 / Width, 1f }, SKShaderTileMode.Clamp);
                using var edge = new SKPaint { Shader = fade, BlendMode = SKBlendMode.DstIn };
                canvas.DrawRect(SKRect.Create(Width, Height), edge);
                canvas.Restore();
            }
            canvas.Restore();
        }

        private static float HighlightWidth(string text, double progress, SKPaint paint, LyricsTextLayout layout)
        {
            int[] starts = StringInfo.ParseCombiningCharacters(text);
            if (starts.Length == 0) return 0;
            double count = progress * starts.Length;
            int completed = (int)Math.Floor(count);
            if (completed >= starts.Length) return layout.MeasureText(paint);
            float before = layout.MeasurePrefix(starts[completed], paint);
            int next = completed + 1 == starts.Length ? text.Length : starts[completed + 1];
            float after = layout.MeasurePrefix(next, paint);
            return before + (after - before) * (float)(count - completed);
        }
    }
}
