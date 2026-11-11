using SkiaSharp;
using WindowsInput.Native;
using WindowsTouchBar.Commands;
using WindowsTouchBar.Components;
using WindowsTouchBar.Models;

namespace WindowsTouchBar.Views;

internal class DefaultFunctionsView : TouchBarView
{
    public override ViewType Type => ViewType.Function;
    public event Action? MediaPageRequested;

    public DefaultFunctionsView(int width, int height) : base(width, height)
    {
        ComponentsGap = 8;
        CreateButtons();
        Update();
    }

    private void CreateButtons()
    {
        var keyCodes = new[]
        {
            VirtualKeyCode.F1,
            VirtualKeyCode.F2,
            VirtualKeyCode.F3,
            VirtualKeyCode.F4,
            VirtualKeyCode.F5,
            VirtualKeyCode.F6,
            VirtualKeyCode.F7,
            VirtualKeyCode.F8,
            VirtualKeyCode.F9,
            VirtualKeyCode.F10,
            VirtualKeyCode.F11,
            VirtualKeyCode.F12
        };

        var mediaPageButton = new PageToggleButton(SKRect.Create(100, Height - 4), "\uE72B", "返回", "Segoe MDL2 Assets");
        mediaPageButton.FillColor = new SKColor(65, 65, 65);
        mediaPageButton.DownFillColor = new SKColor(40, 40, 40);
        mediaPageButton.CornerRadius = new SKPoint(15, 15);
        mediaPageButton.OnTouchUp += (_, _) => MediaPageRequested?.Invoke();
        Components.Add(mediaPageButton);

        Components.AddRange(keyCodes.Select(keyCode =>
        {
            var btn = new Button(SKRect.Create(150, Height - 4));
            btn.Text = keyCode.ToString();
            btn.Command = new KeyPressCommand(keyCode);

            return btn;
        }));
    }
}
