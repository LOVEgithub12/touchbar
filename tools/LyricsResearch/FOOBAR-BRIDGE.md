# foobar2000 playback-state bridge

This bridge targets the installed foobar2000 2.25.5 portable profile and its existing JSplitter / foobox panel. It adds no foobar component, exports playback state, and receives playback commands from the Touch Bar app. It does not modify lyric files.

## Installation

1. Ensure `TouchBar-USBIP\app` exists and is writable by the desktop user. The current installer (`tools/InstallFoobarTouchBarBridge.ps1`) prepends `var TouchBarBridgeOutputPath = "C:\\your-repository\\TouchBar-USBIP\\app";` using the actual repository path. For a manual installation, set that variable before including the bridge, or set `TOUCHBAR_APP_DIR` before starting foobar2000. The variable takes precedence over the environment. Without either, the bridge falls back to relative `TouchBar-USBIP\app\foobar-state.json`. See [installation](../../docs/INSTALL.en.md) / [中文](../../docs/INSTALL.md).
2. Back up `C:\Program Files\foobar2000\profile\foobox\script\js_panels\bottombar.js` before changing it. If `touchbar_bridge.js` already exists in `js_common`, back it up too.
3. Copy `FoobarBridge.js` to `C:\Program Files\foobar2000\profile\foobox\script\js_common\touchbar_bridge.js`. Writing this installation folder may require elevation.
4. Append this line **once**, at the end of `bottombar.js`:

```js
include(fb.ProfilePath + 'foobox\\script\\js_common\\touchbar_bridge.js');
```

5. Reload the JSplitter playback-control panel, or restart foobar2000 after preserving playback position. Play a track with a matching local `.lrc` file, then check the exported JSON and the Touch Bar app.

The bridge uses its own 250 ms panel timer and does not define or replace any playback, paint, mouse or unload callback. Including it twice stops its previous timer before creating one. JSplitter disposes the timer with the panel.

## State contract

The UTF-8 JSON file contains `version` (1), `timestampUtcMs`, `title`, `artist`, `album`, `path`, `positionSeconds`, `durationSeconds`, `isPlaying`, `isPaused`, `lyricsDirectory`, and `embeddedLyrics`. `isPlaying` and `isPaused` preserve foobar's values; both false means stopped. Paused/stopped state gets a heartbeat at most every two seconds. Playback position changes are exported on each 250 ms tick; seeks and state changes are exported immediately on the next tick. Metadata and embedded lyric tags refresh on a track change and every five seconds.

`lyricsDirectory` is the active foobar profile's `lyrics` subfolder. The bridge reads the `SYNCEDLYRICS`, `LYRICS`, and `UNSYNCEDLYRICS` tags and prefers timestamped text. Local LRC lookup and lyric synchronization belong to the Touch Bar app.

JSplitter's `utils.WriteTextFile` is not atomic. The JSON reader must retain its last good snapshot during a brief read/parse failure, and stop presenting the player as active when that snapshot becomes stale (the app uses a 3 second timeout). No network requests are made by this bridge.

## Playback command contract

The bridge reads `foobar-command.json` beside the state file every 250 ms. Its only accepted actions are `previous`, `playpause`, and `next`, dispatched directly to this foobar2000 instance through `fb.Prev()`, `fb.PlayOrPause()`, and `fb.Next()`. These APIs and `utils.ReadTextFile(path, 65001)` are documented in the installed JSplitter `docs\js\foo_spider_monkey_panel.js`.

```json
{"version":1,"commands":[{"id":"a-new-unique-id-for-each-click","action":"playpause","timestampUtcMs":1234567890123}]}
```

The writer must use a new ID for every command. The queue retains at most 16 recent commands in arrival order, so rapid taps between two polls are not overwritten. Already consumed entries are ignored. Commands require a nonempty ID of at most 128 characters, a finite numeric epoch timestamp, and age between zero and 3000 ms. JSON text longer than 4096 characters, future/stale timestamps and unknown actions are ignored. The existing mailbox is consumed without executing it on panel initialization; commands dated before that initialization are also rejected. A consumed ID is not executed again during that panel lifetime, even if its other fields change. Read/parse/API failures are isolated from the playback panel and state export. A single legacy command object remains accepted for compatibility.

## Removal

Remove only the include line above from `bottombar.js`, then reload the panel or restart foobar2000. The bridge file and generated JSON can then be removed. Do not restore an older whole-panel backup if other panel changes have since been made.

## Verification

Run `node tools\LyricsResearch\Test-FoobarBridge.cjs` from the workspace. This exercises the script against a mock JSplitter environment. Live verification still requires playing, pausing and seeking a real track in foobar2000; successful export alone does not verify physical Touch Bar rendering.
