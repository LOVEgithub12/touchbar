# Brightness notification findings

## Final implementation update

The initial findings below are superseded by the final Windows 10 native solution. Matching Microsoft symbols for the installed `twinui.dll` established that `CHardwareButtonFlyout::ShowFlyoutFromShellHook` handles SHELLHOOK 55 by registering brightness notifications and displaying mode 3 (brightness), ignoring lParam. A verified Windows Explorer `NativeHWNDHost` / `DirectUIHWND` hardware flyout accepts this notification. The single-notification probe showed the native 65×140 window in 17 ms without changing focus or brightness. The deployed application now uses `Interop/NativeBrightnessOsd.cs`; the user confirmed the original Windows vertical brightness bar and live value changes. Keyboard backlight is silent, and the custom overlay was removed from the app project. This is a verified Windows 10 implementation detail, not a public cross-version API.

Evidence: `twinui-handlers.txt`, `twinui-symbols.txt`, `../FnKeyResearch/native-brightness-endpoint.md` (workspace tools directory), and `tools/NativeBrightnessOsdProbe/single-notification.log`.

## Earlier investigation (historical)

The application changes built-in screen brightness through `WmiMonitorBrightnessMethods.WmiSetBrightness`. This API updates the panel setting but the existing application does not produce the shell notification that causes the Windows brightness flyout.

No reliable public API for selecting and displaying the native brightness flyout was found. Standard `WM_APPCOMMAND` has no `APPCOMMAND_BRIGHTNESS_UP/DOWN`; HID Consumer usages 0x006f/0x0070 are not ordinary `SendInput` virtual keys.

Evidence reviewed:

- [ModernFlyouts ShellMessageHookHandler](https://github.com/ModernFlyouts-Community/ModernFlyouts/blob/main/ModernFlyouts/Interop/ShellMessageHookHandler.cs) listens for `SHELLHOOK` with `wParam == 55` and classifies it as brightness. This is observation, not a supported API for generating a shell brightness event.
- [ModernFlyouts NativeFlyoutHandler](https://github.com/ModernFlyouts-Community/ModernFlyouts/blob/main/ModernFlyouts.Core/Interop/NativeFlyoutHandler.cs) implements `ShowNativeFlyout()` by restoring an existing `DirectUIHWND`. It neither selects brightness nor supplies its value; restoring a previous volume flyout is not an adequate brightness implementation.
- [TonyTouchBar Native.cs](https://github.com/widmonstertony/TonyTouchBar/blob/main/src/T2TouchBar/Native.cs) also uses WMI for `AdjustBrightness`; it provides no native brightness flyout trigger.

The implemented solution is the application's own top-left desktop indicator in `WindowsTouchBar-master/WindowsTouchBar/Interop/BrightnessOsd.cs`. It runs on its own STA window/message thread, uses a topmost nonactivating tool window, does not take focus, allows pointer input through, scales to primary-screen DPI, coalesces repeated updates, and hides 1.6 seconds after the most recent update. Screen brightness and keyboard backlight share the indicator. Unavailable controls show no fabricated percentage.

Integration:

```csharp
BrightnessOsd.ShowScreen(actualScreenPercent);
BrightnessOsd.ShowKeyboard(actualKeyboardPercent);
BrightnessOsd.ShowUnavailable("键盘背光");
BrightnessOsd.Shutdown(); // application shutdown
```

`ShowScreen` and `ShowKeyboard` should be called only after the caller has successfully changed/read the level. OSD initialization/render failures are isolated from hardware controls and recorded to `WindowsTouchBar.osd.log` next to the executable.

## Validation

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" run --project tools\BrightnessOsdProbe\BrightnessOsdProbe.csproj -c Release --no-restore
```

The probe passed all checks on 2026-11-08: visible nonactivating/click-through/topmost window, focus retention, valid bounds, Unicode screen and keyboard percentage updates, 101 rapid changes, timeout extension, unavailable label, auto-hide, show after hide, and clean shutdown. It does not change any hardware brightness, open a device, or send input. The probe tests the desktop in its own process/session; final user-visible verification belongs to the deployed ordinary-user application.

The downloaded `.cs` files in this research directory are source references only and are not compiled into the application.
