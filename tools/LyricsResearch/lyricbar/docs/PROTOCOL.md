# LyricBar protocol v1

Every LyricBar display ("frontend") is a tiny **WebSocket server on `127.0.0.1`**.
The browser extension connects to each configured endpoint (default
`ws://127.0.0.1:8974`) and pushes JSON text messages. Frontends never need to
talk to Spotify or a lyrics service — they just render.

```
extension ──(ws, JSON)──▶ ws://127.0.0.1:8974   (macOS Touch Bar app)
          ──(ws, JSON)──▶ ws://127.0.0.1:8975   (your frontend)
```

## Rules for frontends

- Listen on **loopback only** (`127.0.0.1`), never `0.0.0.0`.
- Reject handshakes whose `Origin` is an `http(s)://` web page. Accept
  `chrome-extension://`, `moz-extension://`, `safari-web-extension://`, or no
  Origin (native tools such as `scripts/mock-sender.mjs`).
- Ignore unknown message types and unknown fields (forward compatible).
- On connect the extension replays a snapshot (`hello`, `track`, `state`,
  `line`), so a frontend that restarts catches up immediately.

## Messages (extension → frontend)

### `hello`
```json
{ "type": "hello", "v": 1, "client": "extension", "version": "0.1.0" }
```

### `track` — song changed, or its lyrics arrived
Sent first with `"loading": true` and no lines, then again once lyrics resolve.
```json
{
  "type": "track",
  "id": "6Vvjcb1ucRwmHnYga2b9aa",      // Spotify track id when known, else null
  "title": "Song", "artist": "Artist", "album": "Album",
  "durationMs": 212974,
  "loading": false,
  "source": "lrclib",                   // "lrclib" | "spotify" | null (none found)
  "synced": true,                       // false → timings are evenly spread guesses
  "instrumental": false,
  "lines": [ { "t": 12340, "text": "…" }, { "t": 15800, "text": "♪" } ],
  "offsetMs": 500,                      // user's timing nudge for this song (+ = earlier), already applied to `line`
  "match": { "index": 0, "count": 3, "label": "Song — Artist" },  // which lyrics candidate is shown
  "hidden": false,                      // user hid lyrics for this song
  "error": null                         // "network" while the lookup failed and is being retried
}
```
`t` is the start time in ms. Empty/instrumental gaps are `"♪"`.

How to render the states:

| Condition | Suggested display |
|---|---|
| `loading` | song info + "looking up lyrics…" |
| `hidden` | song info + "lyrics hidden for this song" |
| `error == "network"` | song info + "can't reach the lyrics service — retrying…" |
| `instrumental` | song info + "instrumental" |
| `lines` empty | song info + "no lyrics found" |
| `synced: false` | lyrics, but mark them (timing is an even spread, not real) |

### `state` — play / pause / seek / periodic resync (≤ every 10 s)
```json
{ "type": "state", "isPlaying": true, "positionMs": 70123, "ts": 1791169035364 }
```
Current position = `positionMs + (isPlaying ? Date.now() - ts : 0)`.
Frontends that want karaoke-style progress can sync themselves from `track` +
`state`; simple ones can ignore this and use `line`.

### `line` — the active line changed
```json
{ "type": "line", "index": 4, "text": "…", "next": "…", "startMs": 15800, "endMs": 19200 }
```
`index: -1` (with `text: null`) means "before the first line" — show the track
title instead. `next`/`endMs` are `null` on the last line.

### `clear`
The Spotify tab closed or nothing is playing. Show an idle state.

### `ping`
Heartbeat every 20 s. Optionally reply `{ "type": "pong" }`.

## Commands (frontend → extension)

Frontends may send these over the same socket to fix things for the current
song. Choices are remembered per song (in the browser's extension storage),
and the extension answers with an updated `track` (+ `line`).

```json
{ "type": "command", "action": "offset", "deltaMs": 500 }   // lyrics late → show 0.5 s earlier (negative = later)
{ "type": "command", "action": "offset-reset" }
{ "type": "command", "action": "next-match" }               // wrong lyrics → next candidate
{ "type": "command", "action": "toggle-hidden" }            // hide / show lyrics for this song
{ "type": "command", "action": "retry" }                    // forget the cached lookup, search again
```

## Testing a frontend

```sh
node scripts/mock-sender.mjs ws://127.0.0.1:8975          # loops placeholder lines
node scripts/mock-sender.mjs ws://127.0.0.1:8975 --long   # includes an overlong line
# other scenarios: --unsynced --no-lyrics --error --hidden --paused
# the mock also applies commands it receives, like the real extension
```
Then add `ws://127.0.0.1:8975` to the extension's endpoint list (popup → Settings).
