// LRC parsing and lyric-line normalisation.
// A "line" everywhere in LyricBar is { t: startMs, text: string }, sorted by t.

const TIME_TAG = /\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]/g;
const META_TAG = /^\[(ar|ti|al|au|by|re|ve|length|offset):(.*)\]$/i;

/**
 * Parse synced LRC text into sorted lines. Supports multiple time tags per
 * line ("[00:12.00][01:30.00]text"), 2- or 3-digit fractions and [offset:].
 * @param {string} lrc
 * @returns {{t:number,text:string}[]}
 */
export function parseLrc(lrc) {
  if (!lrc) return [];
  let offset = 0;
  const lines = [];
  for (const raw of lrc.split(/\r?\n/)) {
    const row = raw.trim();
    if (!row) continue;
    const meta = META_TAG.exec(row);
    if (meta) {
      if (meta[1].toLowerCase() === 'offset') offset = Number(meta[2]) || 0;
      continue;
    }
    const times = [];
    let lastIndex = 0;
    TIME_TAG.lastIndex = 0;
    let m;
    while ((m = TIME_TAG.exec(row)) && m.index === lastIndex) {
      const frac = m[3] ? Number(m[3].padEnd(3, '0')) : 0;
      times.push(Number(m[1]) * 60000 + Number(m[2]) * 1000 + frac);
      lastIndex = TIME_TAG.lastIndex;
    }
    if (!times.length) continue;
    const text = row.slice(lastIndex).trim();
    for (const t of times) lines.push({ t, text });
  }
  // A positive [offset:] means lyrics should appear earlier.
  for (const line of lines) line.t = Math.max(0, line.t - offset);
  return normalizeLines(lines);
}

/**
 * Spread plain (unsynced) lyrics evenly across the track so they still move.
 * @param {string} text
 * @param {number} durationMs
 */
export function spreadPlainLyrics(text, durationMs) {
  const rows = (text || '').split(/\r?\n/).map((r) => r.trim()).filter(Boolean);
  if (!rows.length) return [];
  const span = Math.max(durationMs || 0, rows.length * 2000);
  const step = span / rows.length;
  return rows.map((row, i) => ({ t: Math.round(i * step), text: row }));
}

/** Sort, trim, mark empty lines as instrumental ("♪"), collapse repeated gaps. */
export function normalizeLines(lines) {
  const sorted = lines
    .filter((l) => Number.isFinite(l.t))
    .map((l) => ({ t: Math.round(l.t), text: (l.text || '').trim() || '♪' }))
    .sort((a, b) => a.t - b.t);
  const out = [];
  for (const line of sorted) {
    const prev = out[out.length - 1];
    if (prev && prev.text === '♪' && line.text === '♪') continue;
    out.push(line);
  }
  return out;
}
