using WindowsInput;
using WindowsInput.Native;
using WindowsTouchBar.Interop;

namespace WindowsTouchBar.Commands;

internal class KeyPressCommand : ITouchCommand
{
    private readonly InputSimulator _inputSimulator;
    private readonly Action<VirtualKeyCode> sendVolumeCommand;

    public VirtualKeyCode[]? KeyCodes { get; set; }

    public VirtualKeyCode[]? ModifierKeyCodes { get; set; }

    public KeyPressCommand(VirtualKeyCode? keyCode = null, Action<VirtualKeyCode>? sendVolumeCommand = null)
    {
        _inputSimulator = new InputSimulator();
        this.sendVolumeCommand = sendVolumeCommand ?? NativeVolumeControl.Send;

        if (keyCode.HasValue)
        {
            KeyCodes = new VirtualKeyCode[] { keyCode.Value };
        }
    }

    // A standalone volume key is a system action, not foreground keyboard input.
    // Keep modified and multi-key shortcuts on their existing keyboard path.
    internal bool IsSystemVolumeCommand => KeyCodes is { Length: 1 } &&
        (ModifierKeyCodes is null or { Length: 0 }) && NativeVolumeControl.IsVolumeKey(KeyCodes[0]);

    public void TouchDown()
    {
        if (IsSystemVolumeCommand)
        {
            sendVolumeCommand(KeyCodes![0]);
            return;
        }
        if (KeyCodes?.Length > 0)
        {
            if (ModifierKeyCodes?.Length > 0)
            {
                _inputSimulator.Keyboard.ModifiedKeyStroke(ModifierKeyCodes, KeyCodes);
            }
            else if (KeyCodes?.Length == 1)
            {
                _inputSimulator.Keyboard.KeyDown(KeyCodes[0]);
            }
            else
            {
                _inputSimulator.Keyboard.KeyPress(KeyCodes);
            }
        }
    }

    public void TouchUp()
    {
        // Shell commands are discrete: release must not inject a key or repeat mute.
        if (IsSystemVolumeCommand) return;
        if (KeyCodes?.Length > 0)
        {
            if (ModifierKeyCodes?.Length is null or 0 &&
                KeyCodes?.Length == 1)
            {
                _inputSimulator.Keyboard.KeyUp(KeyCodes[0]);
            }
        }
    }
}
