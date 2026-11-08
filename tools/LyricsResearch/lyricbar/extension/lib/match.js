// Fuzzy matching between the playing track and lyrics-database records, so a
// search hit for a different song (cover, remix, same title by someone else)
// is rejected instead of shown with the wrong words.

/** Strip decorations Spotify adds that lyrics databases usually lack. */
export function cleanTitle(title) {
  return (title || '')
    .replace(/\s*[-–]\s*(\d{4}\s+)?(remaster(ed)?|live|mono|stereo|radio edit|single version|demo|acoustic|version|from)\b.*$/i, '')
    .replace(/\s*[([](feat\.?|ft\.?|with|from|prod\.?)\b[^)\]]*[)\]]/gi, '')
    .trim();
}

/** Lowercase, drop accents, brackets and punctuation, collapse spaces. */
export function normalize(text) {
  return cleanTitle(text)
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[([].*?[)\]]/g, ' ')
    .replace(/&/g, ' and ')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim();
}

/**
 * 0..1 similarity: 1 for equal strings, 0.9 when one is the other plus a few
 * whole words ("The Example Band" / "Example Band"), else word-overlap (Dice).
 * A short title inside a much longer one ("Stay" / "Stay With Me") is a
 * different song, so it only gets the Dice score.
 */
export function similarity(a, b) {
  const x = normalize(a);
  const y = normalize(b);
  if (!x || !y) return 0;
  if (x === y) return 1;
  const wx = new Set(x.split(' '));
  const wy = new Set(y.split(' '));
  const [short, long, ws, wl] = x.length <= y.length ? [x, y, wx, wy] : [y, x, wy, wx];
  if (short.length >= 4 && ` ${long} `.includes(` ${short} `) && ws.size >= wl.size * 0.6) return 0.9;
  let common = 0;
  for (const w of wx) if (wy.has(w)) common++;
  return (2 * common) / (wx.size + wy.size);
}

/** Split "A, B & C feat. D" into individual artist names. */
export function splitArtists(artist) {
  return (artist || '')
    .split(/\s*(?:,|&|\band\b|\bfeat\.?|\bft\.?|\bx\b|\/)\s*/i)
    .map((a) => a.trim())
    .filter(Boolean);
}

export const MAX_DURATION_DIFF_S = 4;
/** Same song, different edit/release: lyrics likely right but timing may be shifted. */
export const NEAR_DURATION_DIFF_S = 15;

/**
 * Score an LRCLIB record against the playing track.
 * @param {{title:string, artist:string, durationMs:number}} track
 * @param {{trackName?:string, artistName?:string, duration?:number, syncedLyrics?:string}} record
 * @returns {{ok:boolean, near:boolean, score:number}}
 */
export function scoreRecord(track, record) {
  const titleSim = similarity(track.title, record.trackName);
  const artists = splitArtists(track.artist);
  const artistSim = Math.max(
    similarity(track.artist, record.artistName),
    ...artists.map((a) => similarity(a, record.artistName)),
    ...splitArtists(record.artistName).flatMap((ra) => artists.map((a) => similarity(a, ra))),
  );
  const durDiff = track.durationMs && record.duration ? Math.abs(record.duration - track.durationMs / 1000) : null;
  const ok = titleSim >= 0.6 && artistSim >= 0.6 && (durDiff === null || durDiff <= MAX_DURATION_DIFF_S);
  const near = !ok && titleSim === 1 && artistSim >= 0.6 && durDiff !== null && durDiff <= NEAR_DURATION_DIFF_S;
  const score = titleSim * 2 + artistSim + (record.syncedLyrics ? 0.5 : 0) - (durDiff === null ? 0.3 : Math.min(durDiff, 10) * 0.1);
  return { ok, near, score };
}
