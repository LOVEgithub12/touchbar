using SkiaSharp;
using SkiaSharp.Elements;
using WindowsTouchBar.Commands;

namespace WindowsTouchBar.Components;

internal class Button : Rectangle, ITouchComponent, IDisposable
{
    private volatile bool _pressed;
    private bool disposed;
    private readonly object pressLock = new();
    private int pressGeneration;
    private Image? _image;
    private string? _imagePath;
    private Text? _text;
    private float _width;
    private SKColor _defaultColor;
    private float? _textCenterY;

    public event EventHandler? OnTouchDown;
    public event EventHandler? OnTouchUp;
    public ITouchCommand? Command;
    public SKColor DownFillColor = new SKColor(40, 40, 40);
    public float BackgroundBottomInset { get; set; }

    public bool TouchDownInterval { get; set; }
    public float Padding { get; set; } = 25;
    public float? TextCenterY
    {
        get => _textCenterY;
        set
        {
            _textCenterY = value;
            UpdateTextPosition();
        }
    }

    public Button(SKRect size) : base(size)
    {
        CornerRadius = new SKPoint(15, 15);
        FillColor = new SKColor(65, 65, 65);
        BorderWidth = 0;
        _width = size.Width;
    }

    private Text GetText() {

        if (_text is null)
        {
            _text = new Text(string.Empty);
            // SkiaSharp.Elements does not provide Windows font fallback. Use a
            // CJK-capable default for labels copied from localized dialogs.
            // Icon buttons still explicitly select Segoe MDL2 Assets.
            _text.FontFamily = "Microsoft YaHei UI";
            _text.ForeColor = SKColors.White;
            _text.FontSize = 25;
        }

        return _text;
    }

    private void UpdateTextPosition()
    {
        if (_text is not null)
        {
            if (_image is not null)
            {
                var x = (Width - ContentWidth) / 2;
                x += _image.Width + 15;

                _text.Center = new SKPoint(x + _text.Width / 2, _textCenterY ?? Height / 2);
            }
            else
            {
                _text.Center = new SKPoint(Width / 2, _textCenterY ?? Height / 2);
            }
        }
    }

    private void UpdateImagePosition()
    {
        if (_image is not null)
        {
            var factor = _image.Width / _image.Height;

            _image.Height = Height - 4;
            _image.Width = _image.Height * factor;

            if (_text is not null)
            {
                var x = (Width - ContentWidth) / 2;
                
                _image.Center = new SKPoint(x + _image.Width / 2, Height / 2);
            }
            else
            {   
                _image.Center = new SKPoint(Width / 2, Height / 2);
            }
        }
    }

    private void UpdateButton()
    {
        if (_width == 0)
        {
            Width = ContentWidth + Padding * 2;
        }

        UpdateTextPosition();
        UpdateImagePosition();
    }

    private float ContentWidth =>
        (_text?.Bounds.Width ?? 0) +
        (_image?.Width ?? 0) +
        (_text?.Bounds.Width > 0 && _image?.Width > 0 ? 15 : 0);

    public string? Text
    {
        get => _text?.Content;
        set
        {
            GetText().Content = value;
            UpdateButton();
        }
    }

    public string? FontFamily
    {
        get => _text?.FontFamily;
        set
        {
            GetText().FontFamily = value;
            UpdateButton();
        }
    }

    public float? FontSize
    {
        get => _text?.FontSize;
        set
        {
            if (value.HasValue)
            {
                GetText().FontSize = value.Value;
                UpdateButton();
            }
        }
    }

    public SKColor? ForeColor
    {
        get => _text?.ForeColor;
        set
        {
            if (value.HasValue)
            {
                GetText().ForeColor = value.Value;
                UpdateButton();
            }
        }
    }

    public string? ImagePath
    {
        get => _imagePath;
        set
        {
            _imagePath = value;

            if (_image is not null)
            {
                _image.Dispose();
                _image = null;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                string path = Path.IsPathRooted(value) ? value : Path.Combine(AppContext.BaseDirectory, value);
                var bitmap = File.Exists(path) ? SKBitmap.Decode(path) : null;
                if (bitmap is not null)
                {
                    _image = new Image(bitmap)
                    {
                        AlignMode = Image.ImageAlignMode.CenterMiddle,
                        SizeMode = Image.ImageSizeMode.Contain
                    };
                }
            }

            UpdateButton();
        }
    }

    public override void Draw(SKCanvas canvas)
    {
        if (Visible)
        {
            SuspendDrawBeforeAfter();

            DrawBefore(canvas);

            if (BackgroundBottomInset > 0)
            {
                canvas.Save();
                canvas.ClipRect(SKRect.Create(X, Y, Width, Math.Max(0, Height - BackgroundBottomInset)));
                base.Draw(canvas);
                canvas.Restore();
            }
            else base.Draw(canvas);

            if (_text?.Content is not null ||
                _image is not null)
            {
                canvas.Save();
                canvas.Translate(X, Y);

                if (_image is not null)
                {
                    _image.Draw(canvas);
                }

                if (_text?.Content is not null)
                {
                    _text.Draw(canvas);
                }

                canvas.Restore();
            }

            DrawAfter(canvas);
        }
    }

    public virtual void TouchDown(SKPoint _)
    {
        lock (pressLock)
        {
            if (disposed || _pressed) return;

            _pressed = true;
            int generation = ++pressGeneration;
            _defaultColor = FillColor;
            FillColor = DownFillColor;

            if (TouchDownInterval)
            {
                Task.Run(() =>
                {
                    while (true)
                    {
                        lock (pressLock)
                        {
                            if (!_pressed || generation != pressGeneration) return;
                            Command?.TouchDown();
                            OnTouchDown?.Invoke(this, EventArgs.Empty);
                        }
                        Thread.Sleep(50);
                    }
                });
            }
            else
            {
                Command?.TouchDown();
                OnTouchDown?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public virtual void TouchMove(SKPoint point)
    {
        // A slide onto another button (including after a page change) is not a
        // fresh press. Held repeating buttons already have their own worker.
    }

    public virtual void TouchUp(SKPoint _)
    {
        lock (pressLock)
        {
            if (!_pressed) return;
            _pressed = false;
            pressGeneration++;
            FillColor = _defaultColor;

            Command?.TouchUp();
            OnTouchUp?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        lock (pressLock)
        {
            if (disposed) return;
            disposed = true;
            bool wasPressed = _pressed;
            _pressed = false;
            pressGeneration++;
            try
            {
                if (wasPressed)
                {
                    FillColor = _defaultColor;
                    // Cancel a held command without treating destruction as a tap.
                    Command?.TouchUp();
                }
            }
            finally
            {
                _image?.Dispose();
                _image = null;
            }
        }
    }
}
