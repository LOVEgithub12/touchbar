# T2 Touch Bar backlight protocol research

2026-11-08. Read-only descriptor and feature-report probe; no device writes were performed for this research.

## Device

The display transport is Apple `05ac:8302`, but its backlight is a separate HID collection:

- Apple `05ac:8102`, interface `MI_00`, top-level usage `FF12:0001`.
- Feature report 1 is 116 bytes including report ID.
- Byte 1: usage `FF12:0020` (auxiliary mode), 8 bits.
- Byte 2: usage `FF12:0021` (brightness state), 8 bits.
- Other fields and padding must not be guessed. MI_01 / usage `FF00:000F` is the separate keyboard backlight and must be excluded.

`live-read.jsonl` contains actual descriptor/readback evidence. Original report 1 begins `01 05 01 00 00 00 00 E8 03 00 00 ...`. All five feature reports could be read. The returned auxiliary state 5 is outside the descriptor's nominal input values, so it should be preserved as raw bytes for rollback rather than interpreted.

## Established control

Authoritative Linux source: [hid-appletb-bl.c](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-appletb-bl.c), also fetched via [kernel.googlesource mirror](https://kernel.googlesource.com/pub/scm/linux/kernel/git/torvalds/linux/+/master/drivers/hid/hid-appletb-bl.c). Local copies `hid-appletb-bl.c` and `github-hid-appletb-bl.json` contain identical C source.

`appletb_bl_set_brightness` sets auxiliary field to **1** and brightness field to:

| State | Field value |
|---|---:|
| Full/on | 1 |
| Dim | 3 |
| Off | 4 |

The Linux backlight interface maps its public levels 0/1/2 to off/dim/full. It does not expose a continuous percentage. The driver holds the HID open and requests `PM_HINT_FULLON`; when switching off it permits normal power management again.

A scoped Windows implementation can discover this exact collection and validate the descriptor, save the original brightness mode, read and clone the current report and set report ID 1 plus bytes 1/2 to 1, and hold the HID stream open during lyrics. At policy end, original dim/off modes are restored via the established auxiliary=1 command, preserving the latest other payload fields; original full/on mode needs no restore write. Never replay the auxiliary=5 value seen in GET responses as a SET command. Reasserting full/on during lyric playback can counter another Apple service's idle dim/off request without faking keyboard or mouse input. Hardware behavior still requires actual SET/readback and physical idle verification.

Do not claim a numeric brightness increase beyond full/on: the current readback is already nominal brightness state 1. Unknown report 3/4 fields are not established luminance controls and should not be changed merely from descriptor maxima.

Legacy display bulk commands GBTN/SBTN are named in an old T1 reverse-engineering issue, but no verified payload was found locally. They are unnecessary for the supported independent T2 backlight path above.

## Read-only probe

`dotnet run --project tools/TouchBarBrightnessResearch/ReadBacklight.csproj -c Release`

The probe enumerates Apple 8102 descriptors, but opens/reads feature reports only for MI_00. It contains no SetFeature calls and no driver, registry, power-plan or USB binding changes.

## Service and controlled probe

`Interop/TouchBarBacklightService.cs` implements the validated endpoint and policy on a background worker. `SetLyricsVisible(true)` opens the separate backlight collection and requests full/on on entry and renews it every two seconds while lyrics are visible. The worker checks the reported mode every 250 ms during desktop activity. After 20 seconds of desktop idle, the worker waits 16 ms between completed reads to catch a competing fade early. HID failures back off to one second. `HandleTouch` also wakes the bar on every page, retaining full/on while a finger is held and for 30 seconds after its last release. A gesture beginning in off mode is consumed in full, so the first blind touch only wakes the display. A subsequent visible tap operates normally; dim mode accepts the first tap. The touch handler runs before the render lock, with HID I/O serialized separately from the brief policy-state lock.

When neither lyrics nor a touch lease needs the backlight, the connection closes. A saved dim/off mode is restored only if the desktop's `GetLastInputInfo` marker has stayed unchanged. Mouse or keyboard activity during the session prevents restoring a stale off state; no input is synthesized. Errors are deduplicated and do not stop Touch Bar rendering. Dispose must run outside the render lock. RuntimeChecks covers wake gestures, holds, lease renewal/expiry, overlapping policies, desktop activity, transition retries, failed writes, render responsiveness and late callbacks after disposal using a fake connection and virtual clock.

`BacklightServiceProbe.csproj` is read-only by default. Explicit `--roundtrip` is a hardware mutation test: assert full/on once through the service, require successful SET/readback confirmation, release the policy, and verify original mode restored. It does not modify unknown luminance fields, the keyboard backlight, Windows power settings or drivers.

Explicit `--touch-roundtrip` briefly sets the validated backlight to off, sends Down/Move/Up through the production touch-wake policy, confirms full/on and consumption of the complete first gesture, then checks that a second gesture can operate. A `finally` block restores the original brightness mode with auxiliary=1. This tests actual HID commands with simulated touch events; it does not prove that a physical finger reaches the USB/IP input route while dark. For that final check, stop playback, wait for the bar to go fully dark, keep the mouse still, then touch once to wake and lift/touch again to operate.
