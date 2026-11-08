# Physical Fn observations (2026-11-08)

Machine: MacBookPro16,1, Apple keyboard VID 05AC / PID 0340, Windows 10 22H2.
No driver or device setting was changed by these probes.

## Actual report field

The HID descriptor for MI_01 / Col01 has an input report with ID 1 and length
10. Its last byte contains usage `00FF0003`. The installed
`AppleKeyboardInternalUSB.sys` reads `report[9] & 2` at image address
`0x14000601c`. A change to nonzero queues event at context +0x908; zero queues
the event at context +0x920. The worker sends `0xB4042404` / `0xB4042408` to
AppleDFR respectively. These are the actual Fn down/up operations.

The driver also uses `0xB4032074` / `0xB4032078` with KeyManager, but these
follow report byte 1 bit 2 (Alt), not the Fn field.

## User-mode access

The keyboard HID path can be opened with access=0 and sharing read/write.
`HidD_GetInputReport` for report 1 returns `ERROR_INVALID_FUNCTION` (1).
Shared `GENERIC_READ` is denied with error 5 because Windows owns keyboard
input. The same results occur with and without the `\kbd` reference suffix.
All Apple HID collections were enumerated in `hid-descriptors.jsonl`; no
independent collection exposes the Fn field. `QueryDosDevice("AppleDFR")`
returns error 2 while Touch Bar is bound to the USBIP transport. Static
AppleDFR dispatch contains Fn down/up setters and a Fn-behavior setter, but
no state query.

## Observational notification fallback

`\\.\KeyManager`, IOCTL `0xB40320E3`, registers an event handle passed as
the input pointer with zero input length. This API is used by upstream
TonyTouchBar FnKeyService at commit
`3434f67b1c156b4b7b8530db4324cb2c9e64a3c4`.

Static analysis of installed `KeyManager.sys` confirms this adds an event
to a per-file registration list, and closing the file cleans up that list.
The notification is general keyboard activity, not explicit Fn state.
The observed hardware emits duplicate pulses about 0.1–0.8 ms apart for
each report. The user's physical Fn experiment produced pulse pairs with
no standard virtual key down; ordinary typing also produces these pulses.
See `probe/fn-events.log` for the bounded four-minute observational run.

`Helpers/FnKeyService.cs` coalesces duplicates for 5 ms, waits another 20 ms
for ordinary virtual key activity to be observed, rejects pulses within
70 ms of that activity, and toggles inferred state on remaining pulses.
It has no eight-second timeout. It exposes `Reset()` for manual recovery.
It is suitable for standalone Fn press/release but can miss simultaneous
ordinary-key/Fn transitions; it must be presented with a reliable manual
function-view switch. It must not be described as direct Fn-state reading.

Dispose the listener outside any lock taken by its Changed callback.
