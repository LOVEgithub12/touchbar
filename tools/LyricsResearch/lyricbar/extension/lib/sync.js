// Playback-position interpolation and active-line scheduling.

/**
 * Index of the line active at `ms` (last line with t <= ms), or -1 before the first line.
 * @param {{t:number}[]} lines
 * @param {number} ms
 */
export function lineIndexAt(lines, ms) {
  let lo = 0;
  let hi = lines.length - 1;
  let found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (lines[mid].t <= ms) {
      found = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }
  return found;
}

/**
 * Current playback position given the last known state.
 * @param {{isPlaying:boolean, positionMs:number, ts:number}} state
 * @param {number} now epoch ms
 * @param {number} [durationMs]
 */
export function positionAt(state, now, durationMs) {
  if (!state) return 0;
  let pos = state.positionMs + (state.isPlaying ? now - state.ts : 0);
  if (durationMs) pos = Math.min(pos, durationMs);
  return Math.max(0, pos);
}

/** Build the protocol `line` payload for a given index. */
export function linePayload(lines, index) {
  const line = index >= 0 ? lines[index] : null;
  const next = lines[index + 1] ?? null;
  return {
    index,
    text: line ? line.text : null,
    next: next ? next.text : null,
    startMs: line ? line.t : 0,
    endMs: next ? next.t : null,
  };
}

/**
 * Emits `onLine(payload)` whenever the active lyric line changes. Uses a
 * single timeout aimed at the next line boundary instead of polling.
 */
export class LyricSync {
  /**
   * @param {(payload: ReturnType<typeof linePayload>) => void} onLine
   * @param {{now?: () => number, setTimer?: typeof setTimeout, clearTimer?: typeof clearTimeout}} [deps]
   */
  constructor(onLine, deps = {}) {
    this.onLine = onLine;
    this.now = deps.now ?? (() => Date.now());
    this.setTimer = deps.setTimer ?? ((fn, ms) => setTimeout(fn, ms));
    this.clearTimer = deps.clearTimer ?? ((id) => clearTimeout(id));
    this.lines = [];
    this.state = null;
    this.durationMs = 0;
    this.offsetMs = 0;
    this.index = null;
    this.timer = null;
  }

  /** Positive offset shows lyrics earlier. */
  setOffset(ms) {
    this.offsetMs = Number(ms) || 0;
    this.tick();
  }

  setLines(lines, durationMs = 0) {
    this.lines = lines || [];
    this.durationMs = durationMs;
    this.index = null;
    this.tick();
  }

  setState(state) {
    this.state = state;
    this.tick();
  }

  current() {
    return this.index === null ? null : linePayload(this.lines, this.index);
  }

  stop() {
    if (this.timer !== null) this.clearTimer(this.timer);
    this.timer = null;
  }

  reset() {
    this.stop();
    this.lines = [];
    this.state = null;
    this.index = null;
  }

  tick() {
    this.stop();
    if (!this.lines.length || !this.state) return;
    const pos = positionAt(this.state, this.now(), this.durationMs) + this.offsetMs;
    const index = lineIndexAt(this.lines, pos);
    if (index !== this.index) {
      this.index = index;
      this.onLine(linePayload(this.lines, index));
    }
    const next = this.lines[index + 1];
    if (this.state.isPlaying && next) {
      // +5ms so we land just past the boundary rather than just before it.
      const wait = Math.max(10, next.t - pos + 5);
      this.timer = this.setTimer(() => this.tick(), wait);
    }
  }
}
