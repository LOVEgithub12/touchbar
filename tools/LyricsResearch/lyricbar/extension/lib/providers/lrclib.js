// LRCLIB provider — https://lrclib.net (free, open, no auth).
import { parseLrc, spreadPlainLyrics } from '../lrc.js';
import { cleanTitle, scoreRecord } from '../match.js';

export { cleanTitle };

const BASE = 'https://lrclib.net/api';
const MAX_CANDIDATES = 5;

/**
 * Turn an LRCLIB record into a LyricBar lyrics option, or null when it has none.
 * Every option carries a stable `key` so the user's choice can be remembered.
 */
export function fromRecord(record, durationMs) {
  if (!record) return null;
  const base = {
    key: `lrclib:${record.id ?? `${record.trackName}|${record.artistName}`}`,
    source: 'lrclib',
    label: [record.trackName, record.artistName].filter(Boolean).join(' — '),
  };
  if (record.instrumental) return { ...base, synced: true, instrumental: true, lines: [] };
  if (record.syncedLyrics) {
    const lines = parseLrc(record.syncedLyrics);
    if (lines.length) return { ...base, synced: true, lines };
  }
  if (record.plainLyrics) {
    const lines = spreadPlainLyrics(record.plainLyrics, durationMs);
    if (lines.length) return { ...base, synced: false, lines };
  }
  return null;
}

/** Keep records that really are this song, best first (synced, closest duration). */
export function rankRecords(track, records) {
  return (records || [])
    .map((r) => ({ r, ...scoreRecord(track, r) }))
    .filter((x) => x.ok)
    .sort((a, b) => b.score - a.score)
    .map((x) => x.r);
}

/** Records that are this song but whose length is a bit off (other edit/release). */
export function nearRecords(track, records) {
  return (records || [])
    .map((r) => ({ r, ...scoreRecord(track, r) }))
    .filter((x) => x.near && (x.r.syncedLyrics || x.r.plainLyrics))
    .sort((a, b) => b.score - a.score)
    .map((x) => x.r);
}

/**
 * Find every plausible lyrics option for a track, best first.
 * @param {{title:string, artist:string, primaryArtist?:string, album?:string, durationMs:number}} track
 * @param {{fetch?: typeof fetch, clientId?: string, signal?: AbortSignal}} [opts]
 * @returns {Promise<Array<{key:string, source:'lrclib', label:string, synced:boolean, lines:{t:number,text:string}[], instrumental?:boolean}>>}
 */
export async function findLrclibOptions(track, opts = {}) {
  const doFetch = opts.fetch ?? fetch;
  const headers = { 'Lrclib-Client': opts.clientId ?? 'LyricBar (https://github.com/reihnxx/lyricbar)' };
  const artist = track.primaryArtist || track.artist;
  const get = async (path, params) => {
    const res = await doFetch(`${BASE}/${path}?${new URLSearchParams(params)}`, { headers, signal: opts.signal });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`LRCLIB ${path} HTTP ${res.status}`);
    return res.json();
  };

  const records = [];
  const seen = new Set();
  const add = (list) => {
    for (const r of list) {
      const id = r.id ?? `${r.trackName}|${r.artistName}|${r.duration}`;
      if (!seen.has(id)) {
        seen.add(id);
        records.push(r);
      }
    }
  };

  // 1) Exact signature (title, artist, album, duration ±2 s) — trusted as-is.
  if (track.album && track.durationMs) {
    const exact = await get('get', {
      track_name: track.title,
      artist_name: artist,
      album_name: track.album,
      duration: String(Math.round(track.durationMs / 1000)),
    });
    if (exact) add([exact]);
  }

  // 2) Searches, broadening only while nothing good has been found.
  const clean = cleanTitle(track.title);
  const searches = [
    { track_name: track.title, artist_name: artist },
    ...(clean !== track.title ? [{ track_name: clean, artist_name: artist }] : []),
    { q: `${clean} ${artist}` },
  ];
  const nearby = [];
  for (const params of searches) {
    const hasSynced = records.some((r) => r.syncedLyrics || r.instrumental);
    if (hasSynced) break;
    const found = (await get('search', params)) ?? [];
    add(rankRecords(track, found));
    nearby.push(...nearRecords(track, found));
  }
  // Nothing matches the exact length: offer same-title records of a slightly
  // different length (the user can nudge timing or cycle matches).
  if (!records.length) add(nearby.slice(0, MAX_CANDIDATES));

  const options = [];
  for (const record of records) {
    const option = fromRecord(record, track.durationMs);
    if (option) options.push(option);
  }
  // Synced first; keep relative (score) order otherwise.
  options.sort((a, b) => Number(b.synced) - Number(a.synced));
  return options.slice(0, MAX_CANDIDATES);
}

/** Best single option, or null. */
export async function fetchLrclib(track, opts = {}) {
  return (await findLrclibOptions(track, opts))[0] ?? null;
}
