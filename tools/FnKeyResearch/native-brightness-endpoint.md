# Native brightness flyout endpoint research

Read-only analysis of the Windows 10 DLLs installed on this machine. No
messages, IOCTL writes, WNF publications, or internal functions were invoked.

## Confirmed receiver

Microsoft's matching twinui PDB (obtained by the brightness agent) identifies
`CHardwareButtonFlyout::Launch` at RVA `0x3e7eb0`.

Its instructions:

1. Call a factory at `0x3e7240`, which constructs a subclass of
   `DirectUI::NativeHWNDHost` and calls `NativeHWNDHost::Initialize`.
2. Call `NativeHWNDHost::GetHWND`, storing the result at object offset 0x60.
3. Call `RegisterWindowMessageW(L"SHELLHOOK")`, storing the message ID at 0x14.
4. Call `RegisterShellHookWindow` on that exact HWND.

Therefore, an already-created native hardware flyout's top-level
`NativeHWNDHost` is directly registered to receive shell hook messages.
It is not necessary to guess another shell window if this receiver exists.
The established ModernFlyouts implementation identifies it as a
`NativeHWNDHost` with a `DirectUIHWND` child owned by the shell process.

The factory passes null window title/class to the DirectUI host initializer
(default NativeHWNDHost), extended style `0x08080008`, popup style
`0x80000000`, and no parent. Initial dimensions are 50 by 60 before layout.

Evidence: `twinui-endpoints.txt`. The brightness agent is separately decoding
the exact `SHELLHOOK` parameters accepted by `ShowFlyoutFromShellHook`; the
receiver evidence alone is not authorization to send guessed messages.

## Other paths investigated

`hidserv.dll` does not contain a consumer brightness 0x6F/0x70 dispatch branch
or import a brightness/powrprof API. Its one WNF publication is for other
usages; its ordinary consumer mappings feed virtual keys or WM_APPCOMMAND.
This DLL does not supply a direct brightness-flyout entry point.

`powrprof!PowerSetBrightnessAndTransitionTimes` checks a non-null input pointer
and packages 32 bytes into `NtPowerInformation` level 0x57, operation 0x18.
No shell-message call appears in that wrapper. It was not executed, and
there is no evidence from this wrapper alone that it shows the flyout.

`PowerSetAlsBrightnessOffset` publishes WNF state. Its semantics are an ALS
offset, and it was not used as a speculative flyout trigger.

## Cold start when the hardware flyout does not exist yet

Additional matching-PDB evidence identifies the creation path:

- `CAudioFlyoutController::SubscribeToServices` (RVA 0x565d0) registers shell
  hook numbers **12, 50, 55** with `IImmersiveShellHookService`; the three
  64-bit constants are at RVA 0x501f20.
- `CAudioFlyoutController::OnShellHookMessage` (0x3daea0) checks
  `ShouldShowFlyoutFromShellHook`, calls `_EnsureFlyout` (0x3db2e0), then
  calls `ShowFlyoutFromShellHook`. Thus the controller can create the
  hardware flyout from a shell hook before a NativeHWNDHost exists.
- `CImmersiveShellHookService` registers `SHELLHOOK` and subscribes to
  `IImmersiveWindowMessageService` for its message ID.
- `CImmersiveWindowMessageService::v_PublishServices` (0x9f3f0) calls
  `SHCreateWorkerWindowW` with callback
  `CImmersiveWindowMessageService::s_MessageMsgWndProc` (0x602a0), no parent,
  and zero styles. This creates a hidden **WorkerW** rather than a uniquely
  named `ImmersiveShellGlobalClass` in this code path.
- Its message procedure forwards subscribed messages to the shell hook
  service and thence to the audio/brightness controller.

`IImmersiveWindowMessageService` IID/SID is
`bf8b6ede-2069-4010-a9e7-4480e24c01ca` on this build. Its implementing object
also implements `IObjectWithWindow`, with `GetWindow` at RVA 0xa1ef0. A future
read-only COM query could potentially identify the exact WorkerW instead
of guessing among other windows with the same class. That query, its
cross-process availability, and the interface IID were **not verified**.

The cold-start chain is static evidence only. No existing flyout was
destroyed, Explorer was not restarted, and no messages were sent to WorkerW.
Do not claim that a cold login with no prior hardware flyout was validated.
The separate native brightness probe successfully tested the already-created
NativeHWNDHost route, and the user subsequently confirmed the actual official
brightness bar and changing value.

Supporting files: `twinui-cold-start.txt`, `twinui-cold-window.txt`,
`twinui-window-symbols.txt`, `twinui-window-iid-symbols.txt`.
