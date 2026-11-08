// Spotify provider (experimental, opt-in).
//
// LyricBar never calls Spotify's private API itself. When enabled, a page
// script observes the lyrics response the Web Player already downloads for
// its own Now Playing view and hands it to us. This file only parses it.
import { normalizeLines, spreadPlainLyrics } from '../lrc.js';

/**
 * Album-art identity: Spotify image ids are "<16-char size prefix><hash>".
 * The hash is shared between sizes, so it ties a lyrics response (which
 * embeds the art URL) to the track shown in the player.
 * @param {string} url
 */
export function artHash(url) {
  const id = /\/image\/([0-9a-f]{24,})/i.exec(url || '')?.[1];
  return id && id.length > 16 ? id.slice(16).toLowerCase() : null;
}

/**
 * @param {any} body color-lyrics JSON response
 * @param {number} durationMs
 */
export function fromColorLyrics(body, durationMs) {
  const lyrics = body?.lyrics;
  if (!lyrics || !Array.isArray(lyrics.lines) || !lyrics.lines.length) return null;
  if (lyrics.syncType === 'LINE_SYNCED' || lyrics.syncType === 'SYLLABLE_SYNCED') {
    const lines = normalizeLines(
      lyrics.lines.map((l) => ({ t: Number(l.startTimeMs), text: l.words })),
    );
    return lines.length ? { key: 'spotify', source: 'spotify', label: 'Spotify', synced: true, lines } : null;
  }
  const plain = lyrics.lines.map((l) => l.words).join('\n');
  const lines = spreadPlainLyrics(plain, durationMs);
  return lines.length ? { key: 'spotify', source: 'spotify', label: 'Spotify', synced: false, lines } : null;
}
