# Features, usage and troubleshooting

[简体中文](USAGE.md) · [Home](../README.en.md) · [Installation](INSTALL.en.md)

## Pages and touch

| Context | Interaction / result |
|---|---|
| Main media bar | Fixed left F / 1–12 switch, display brightness, keyboard backlight, playback controls and volume; main controls shifted right by 20 px |
| Function page | Enter using the fixed icon; evenly sized F1–F12 keys and a left-side return button |
| Lyric playback | Active foobar2000 playback uses the lyric page, retaining playback, player shortcut, mute and volume |
| Paused / stopped | Leave the lyric page; configured foreground application controls can take priority, otherwise show the main bar |
| Browser during playback | Keep the current media/lyric context instead of replacing it with browser shortcuts |
| Temporary brightness access | Tap the player icon to open the main bar; return after four idle seconds during playback, measured from the last actual touch release |
| Holding a button | Repeat supported volume/brightness actions; mute remains a single toggle; releasing or disposing ends the command |
| Drags and stray releases | Page/background changes require a completed valid tap; sliding into a control or a duplicate release does not start another action |

The explicit F-page state belongs to the app rather than a missing physical Fn release event. This removes the earlier inferred-Fn path that could leave F1–F12 selected. Legacy inference helpers and tests remain as reference material.

![Main media bar](images/media.png)
![Function keys](images/function-keys.png)

## Lyrics and backgrounds

The bridge samples playback state every 250 ms. The renderer interpolates using a monotonic clock and evaluates the lyric timeline each frame, avoiding highlighting that advances only on bridge polls. Seeks, track changes and line changes reset the relevant state.

Lookup prefers a sidecar LRC beside the track, then embedded timestamped lyrics, then the profile's lyric directory. Title/artist metadata is checked; conflicting or ambiguous candidates are not chosen blindly. LRC supports line timestamps, multiple timestamps, offset and enhanced word timestamps. Line-only timing allows estimated within-line highlighting; explicit word timestamps provide source-defined word timing.

Lyrics use one line, with purple/blue sung text and white unsung text. Text starts at 34 px, reduces to 28 px when needed, then scrolls smoothly with the singing front. There is no next-line preview or bottom progress bar. Mixed scripts use installed-font fallback; missing language fonts must still be installed on the system.

**Tap the lyric text area** to cycle the following modes. The completed release applies the selection, which is saved locally:

| Mode | Behavior |
|---|---|
| Aurora | Purple, blue and cyan ribbons with a softer energy envelope, a stronger center, faint fine upper/lower waves, blended edges, star points and side dust |
| Spectrum | Wide spectrum driven by the actual shared-mix FFT, with faster fill response; particle layers have independent height, delay, dwell and fade, rather than moving sideways as one sheet |
| Off | Remove the music background while retaining lyrics and media controls; this does not turn off the panel or hide lyrics |

Both backgrounds retain side stardust pulses. Side effects stay above the original button bottom edge. With a background enabled, button fills are shortened by 6 px only at the bottom; top edges and touch areas stay in place. The lyric page continuously requests full backlight to prevent periodic dimming.

### Audio input

- WASAPI shared-output loopback; only derived energy/spectrum is retained, with no microphone recording or stored PCM.
- 512 logarithmic bands from 32 Hz to approximately 16.5 kHz; 4096-point bass and 2048-point mid/treble windows combine channel power to avoid opposite-phase cancellation.
- Analysis updates around 60 Hz. Rendering and transfer also depend on device acknowledgments and system load; this is not a guaranteed hardware frame rate.
- The endpoint mix is not vocal separation. Accompaniment can still produce movement when vocals stop. Actual silence decays; already emitted particles can briefly remain before disappearing.
- Exclusive WASAPI / ASIO may bypass the shared mix. Use an output mode that shared loopback can capture for visualization.

## Custom application controls

Edit **the runtime copy**, `TouchBar-USBIP\app\settings.json`; the app watches for configuration changes. The source `settings.json` is an initial template, while the build helper preserves existing runtime settings.

Example: Chrome back and new-tab controls:

```json
{
  "views": [
    { "type": "media", "default": true },
    { "type": "function" },
    {
      "type": "custom",
      "application": "chrome.exe",
      "components": [
        { "type": "button", "title": "Back", "keyCodes": ["LEFT"], "modifierKeyCodes": ["MENU"], "width": 150 },
        { "type": "spacer" },
        { "type": "button", "title": "New tab", "keyCodes": ["VK_T"], "modifierKeyCodes": ["CONTROL"], "width": 200 }
      ]
    }
  ]
}
```

Match the actual foreground process name. Key names follow `WindowsInput.Native.VirtualKeyCode`; `icon` can contain a Segoe MDL2 Assets character. Playback priority still applies, so pause playback before checking browser shortcuts. Explorer OK/Cancel/Apply support is restricted to Explorer dialogs with recognized native BUTTON controls, not every Windows dialog.

## Troubleshooting

| Symptom | What to check |
|---|---|
| Missing application | Run `tools\Build.ps1 -PrepareRuntime`; verify `TouchBar-USBIP\app\WindowsTouchBar.exe` exists |
| pwsh not found | Install PowerShell 7, reopen the session and check `Get-Command pwsh` |
| Black panel / startup fails | Inspect `launcher.log` and `app\WindowsTouchBar.status.log`; check usbipd service, exact device and VBoxUSB binding; avoid duplicate instances |
| Device Code 37 after boot | Compare Device Manager and recovery logs; use the boot recovery tool only for the matching confirmed failure |
| Chinese USB names break login startup | This version parses usbipd JSON explicitly as UTF-8; update the helper and startup scripts together, not only the EXE |
| App returns after Stop | Ensure `Stop.ps1`, `AutoStart.ps1` and CMD entry points are from the same version; Stop cancels initializing helpers as well as the main app |
| Touch feedback but no shortcut | Run as the normal desktop user; elevated target applications can block ordinary injected keys through UIPI |
| Volume fails over Task Manager | Standalone volume uses Shell system commands in this version; confirm the new `NativeVolumeControl` was built; ordinary compound shortcuts retain keyboard semantics |
| Missing / stale lyrics | Check bridge app path, regularly updated `foobar-state.json`, LRC timing and metadata; reconfigure bridge paths after moving the repository |
| Browser does not replace playback controls | Intended during playback; test **after pausing**, with a matching process configured in settings |
| Spectrum does not move | Check background is not Off, playback is active and output can be loopback-captured; try shared output instead of exclusive/ASIO |
| Missing script glyphs | Install the appropriate system language fonts; FontChecks tests current glyph coverage |

Important local files:

| File | Purpose |
|---|---|
| `TouchBar-USBIP\launcher.log` | Manual launch result |
| `TouchBar-USBIP\autostart.log` | Login waits, retries and cancellation |
| `TouchBar-USBIP\app\WindowsTouchBar.status.log` | Transport initialization, size and first-frame acknowledgment |
| `TouchBar-USBIP\app\WindowsTouchBar.error.log` | Application errors, if any |
| `TouchBar-USBIP\app\foobar-state.json` | Local player state; may contain song metadata and file paths |
| `TouchBar-USBIP\app\visualizer-preference.json` | Selected background |
| `%ProgramFiles%\TouchBar-Startup\boot-recovery.log` | Separate system recovery log, when installed |

Before reporting an issue, remove personal paths, song metadata and device serials. Include OS, model, commit, reproduction steps and the relevant error excerpt.
