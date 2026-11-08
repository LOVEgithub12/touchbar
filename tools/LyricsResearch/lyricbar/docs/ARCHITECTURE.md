# Architecture

```
┌──────────── Browser (Brave / Chrome / Edge / Arc) ────────────┐
│ open.spotify.com tab                                           │
│  content/spotify-main.js (page world, opt-in)                  │
│     └─ sees the lyrics response the Web Player already loads   │
│  content/spotify.js (isolated world)                           │
│     └─ reads title / artist / album / duration / position      │
│        from the player DOM (content/selectors.js)              │
│                 │  chrome.runtime port                         │
│ background/service-worker.js  ◀── core                         │
│     ├─ lib/providers/lrclib.js   LRCLIB lookup (default)       │
│     ├─ lib/providers/spotify.js  parse Spotify lyrics (opt-in) │
│     ├─ lib/cache.js              LRU + storage.session         │
│     ├─ lib/sync.js               active line, timer per line   │
│     └─ lib/transport.js          WebSocket fan-out, reconnect  │
└───────────────────────────────│────────────────────────────────┘
                                │ JSON over ws://127.0.0.1 (docs/PROTOCOL.md)
            ┌───────────────────┼─────────────────────┐
     apps/macos (Touch Bar   future: Dock tile,   future: Windows taskbar,
     + menu bar, Swift)      Linux bars, OBS…      Raycast, e-ink, …

In-browser display (every OS): the floating lyrics window. The Spotify tab
injects a button into the player bar; clicking it opens a Document
Picture-in-Picture window (always on top) rendered by content/mini-render.js,
fed by a `display` runtime port with the same messages as docs/PROTOCOL.md.
ui/mini.html is the fallback (normal window + "Keep on top").
```

## Why this shape

- **The browser is the only place that knows what the Web Player is doing**,
  on every OS. Putting the "brain" in the extension means every frontend is a
  dumb renderer that can be written in any language in ~100 lines.
- **Position comes from the progress bar's CSS variable**
  (`--progress-bar-transform`). Spotify updates it once a second with the
  position it is animating *towards* over `--progress-bar-duration` ms, so the
  real position at that instant is `percent × duration − transition`. A
  `MutationObserver` timestamps each update, giving ~20 ms accuracy without
  touching the audio pipeline. This also works for Spotify Connect (music
  playing on your phone or desktop app while the web player is open).
- **The service worker schedules one timer per line** (`LyricSync`) rather than
  polling, and only talks to frontends when something changes.
- **WebSockets are opened from the service worker**, not the page, so Chrome's
  Local Network Access prompt never appears on open.spotify.com.
- **The service worker stays alive** because the tab sends a resync at least
  every 10 s and the transport pings frontends every 20 s (Chrome ≥ 116 keeps
  workers with active WebSocket traffic alive).

## macOS app

- `TouchBarPrivate` wraps private AppKit/DFRFoundation calls
  (`+[NSTouchBarItem addSystemTrayItem:]`,
  `+[NSTouchBar presentSystemModalTouchBar:systemTrayItemIdentifier:]`,
  `DFRElementSetControlStripPresenceForIdentifier`). These are what every
  global Touch Bar tool (MTMR, Pock, BetterTouchTool…) uses; there is no public
  API for a background app to draw on the Touch Bar. Each call is guarded with
  `respondsToSelector:` / `dlsym`, so a missing API degrades to menu-bar-only.
- `LyricServer` — `Network.framework` WebSocket listener bound to 127.0.0.1.
- `LyricStore` → `Display` → `TouchBarController` (Control Strip ♪ button +
  full-width modal bar) and `StatusItemController` (menu bar).
- No dependencies, no Xcode project: `swift build` + `scripts/build-macos.sh`.

### Touch Bar gotchas (learned the hard way)

- Give the lyric item a **frame-based view** (autoresizing mask, no width
  constraints, no `intrinsicContentSize` override). The system-modal bar then
  stretches it to fill everything left of the Control Strip. A view with
  `translatesAutoresizingMaskIntoConstraints = false` + low-priority width
  constraints collapses to zero width and nothing shows.
- While the modal bar is up, the Control Strip slot of its tray item shows a
  generic icon; the ♪ button is visible again once the user closes the bar
  with the system ⊗. There is no "closed" callback, so the app doesn't track
  visibility — presenting an already visible bar is harmless.
- `screencapture -b file.png` captures the Touch Bar (it's black while the
  Touch Bar is dimmed after inactivity) — handy for checking layout.
