using SkiaSharp;
using WindowsTouchBar.Commands;
using WindowsTouchBar.Components;
using WindowsTouchBar.Models;
using WindowsInput.Native;

namespace WindowsTouchBar.Views;

internal class CustomFunctionsView : TouchBarView
{
    public override ViewType Type => ViewType.Custom;
    public event Action? MainRequested;

    public CustomFunctionsView(int width, int height) : base(width, height)
    {
    }

    public override void ConfigureView(Settings.View? view)
    {
        base.ConfigureView(view);

        foreach (var component in Components.OfType<IDisposable>()) component.Dispose();
        Components.Clear();
        
        if (view?.Components != null)
        {
            ConfigureComponents(view.Components,
                string.Equals(view.Application, "chrome.exe", StringComparison.OrdinalIgnoreCase));
        }        

        Update();
    }

    private void ConfigureComponents(IEnumerable<Settings.Component> components, bool chrome)
    {
        bool mainAdded = false;
        foreach (var c in components)
        {
            IComponent? component = null;
            switch (c.Type)
            {
                case Settings.ComponentType.Button:
                    component = BuildButton(c);
                    break;
                case Settings.ComponentType.Spacer:
                    component = BuildSpacer(c);
                    break;
            }

            if (component != null)
            {
                Components.Add(component);
                if (chrome && !mainAdded && c.Type == Settings.ComponentType.Button &&
                    c.KeyCodes is { Length: 1 } keys && keys[0] == VirtualKeyCode.F5 &&
                    (c.ModifierKeyCodes?.Length ?? 0) == 0)
                {
                    var main = new ChromeMainButton(SKRect.Create(112, Height - 4));
                    main.OnTouchUp += (_, _) => MainRequested?.Invoke();
                    Components.Add(main);
                    mainAdded = true;
                }
            }
        }
    }

    private Button BuildButton(Settings.Component component)
    {
        var btn = new Button(SKRect.Create(component.Width ?? 0, Height - 4));

        if (!string.IsNullOrWhiteSpace(component.Image))
        {
            btn.ImagePath = component.Image;
        }
        
        if (!string.IsNullOrWhiteSpace(component.Icon) || !string.IsNullOrWhiteSpace(component.Title))
        {
            btn.Text = component.Icon ?? component.Title;

            if (!string.IsNullOrWhiteSpace(component.TextColor) && SKColor.TryParse(component.TextColor, out var textColor))
            {
                btn.ForeColor = textColor;
            }

            if (!string.IsNullOrWhiteSpace(component.Icon))
            {
                btn.FontFamily = "Segoe MDL2 Assets";
            }
        }

        if (!string.IsNullOrWhiteSpace(component.Color) && SKColor.TryParse(component.Color, out var fillColor))
        {
            btn.FillColor = fillColor;
        }

        btn.Command = new KeyPressCommand
        {
            KeyCodes = component.KeyCodes,
            ModifierKeyCodes = component.ModifierKeyCodes
        };

        return btn;
    }

    private Spacer BuildSpacer(Settings.Component component)
    {
        return component.Width > 0 ? new Spacer(component.Width.Value) : new Spacer(true);
    }

    private sealed class ChromeMainButton : Button
    {
        // Shared immutable resource: reconfiguring the foreground view does not
        // repeatedly decode or leak a native bitmap for each browser visit.
        private static readonly Lazy<SKBitmap?> icon = new(() =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Resources", "chrome.png");
            return File.Exists(path) ? SKBitmap.Decode(path) : null;
        });

        public ChromeMainButton(SKRect size) : base(size)
        {
            if (icon.Value is null) { Text = "Chrome"; FontSize = 20; }
        }

        public override void Draw(SKCanvas canvas)
        {
            base.Draw(canvas);
            if (!Visible || icon.Value is not SKBitmap bitmap) return;
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            const float iconSize = 43.2f;
            canvas.DrawBitmap(bitmap, SKRect.Create(X + (Width - iconSize) / 2,
                Y + (Height - iconSize) / 2, iconSize, iconSize), paint);
        }
    }
}
