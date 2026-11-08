# T2 keyboard backlight verification (2026-11-08)

Machine: MacBookPro16,1, Windows 10. Original Apple keyboard driver remains installed.

Working interface: HID vendor `05ac`, product `8102`, interface `01`, top-level usage `FF00000F`. This is a **separate keyboard-backlight collection**; interface 00 is the Touch Bar backlight and is excluded.

Protocol corroboration: [Linux hid-apple.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-apple.c), `apple_magic_backlight_report_set`, `apple_magic_backlight_set`, and `apple_magic_backlight_init`. The fetched source is saved as `hid-apple.c`. The implementation uses protocol facts and the live HID descriptor.

Live feature descriptors:

- Report 1, 9 bytes: report ID, little-endian 32-bit brightness (minimum 100, maximum 14660), little-endian 32-bit transition.
- Report 3, 9 bytes: report ID, 8-bit power (0/1), little-endian 32-bit transition, 24 padding bits.
- Transition is `0x5e | (rate << 8)`. The app uses rate 0 to avoid long fades during repeated button presses.
- GET report 1 succeeds. This device replaces the returned report ID with 0; brightness remains at byte offset 1.
- GET report 3 fails. Initial brightness percentage therefore represents the saved device level, not a verified power state. The app tracks power-off requests made during its own lifetime and logs this limitation.

Hardware verification:

1. Initial GET report 1: `00 46 19 00 00 00 00 00 00`, raw level 6470.
2. Direct report 1 small increase to 6770: SET accepted, GET after transition returned 6769 (device quantization).
3. Restore report 1 to 6470: GET returned 6470.
4. Actual `KeyboardBacklightAPI.Adjust(5)`: power report 3 accepted, brightness report 1 accepted, confirmed 49%, operation took 201 ms including discovery/open/readback. Requested raw 7183; accepted readback within 0.1% tolerance.
5. Restored raw 6470. An immediate read still showed the prior value; a subsequent independent read confirmed **6470**. The probe now waits for settling during restoration.

Power-off is implemented from the established protocol but was not physically tested during this session. There is no initialization write or persistent HID handle. Human confirmation of LED appearance remains the final physical check.

Rejected alternatives (read-only probes):

- `AppleKeyboardInternalUSB` IOCTL `0xB403204C` returned success and seven zero bytes, indicating no support for Boot Camp's older backlight interface.
- `MacHALDriver` queries for SMC key `LKSB` (`0x9C402460`, `0x9C402454`) returned Win32 error 31. No MacHAL writes were performed.
- Third-party Macs Fan Control AppleSMC driver was not used.

App source: `WindowsTouchBar-master/WindowsTouchBar/Interop/KeyboardBacklightAPI.cs`. It exposes `Adjust(int)` and read-only `GetCurrentBrightness()`. The only additional dependencies are the app's existing HidSharp and ControlDiagnostics classes.

Read-only probe: `KeyboardBacklightProbe.exe --api-read`. The plain probe prints descriptors and GET reports. `--roundtrip` and `--api-roundtrip` are explicit hardware validation modes that alter brightness and restore it; do not use them as routine startup checks.
