// Run with Node.js; this never opens or changes the installed foobar2000 player.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, 'FoobarBridge.js'), 'utf8');
let now = 10000;
let nextTimer = 0;
let failWrites = false;
let throwWrites = false;
let throwReads = false;
let commandText = JSON.stringify({ id: 'present-at-startup', action: 'next', timestampUtcMs: now });
const commands = [];
const timers = new Map();
const writes = [];
const errors = [];
const originalCallback = () => {};
let fields = { SYNCEDLYRICS: '', LYRICS: 'plain text', UNSYNCEDLYRICS: '[00:01.00]中文歌词' };
const handle = {
    Path: 'D:\\Music\\歌.flac', RawPath: 'file://D:\\Music\\歌.flac', SubSong: 0,
    GetFileInfo() {
        const names = Object.keys(fields);
        return {
            MetaFind(name) { return names.indexOf(name); },
            MetaValueCount() { return 1; },
            MetaValue(index) { return fields[names[index]]; }
        };
    }
};
const sandbox = {
    Date: { now: () => now },
    console: { log: value => errors.push(String(value)) },
    on_playback_new_track: originalCallback,
    on_playback_pause: originalCallback,
    fb: {
        IsPlaying: false, IsPaused: false, PlaybackTime: 0, PlaybackLength: 180,
        ProfilePath: 'C:\\Player\\profile\\',
        GetNowPlaying: () => handle,
        Prev() { commands.push('previous'); },
        PlayOrPause() { commands.push('playpause'); },
        Next() { commands.push('next'); },
        TitleFormat(format) {
            return { EvalWithMetadb() { return format.includes('title') ? '歌名' : format.includes('artist') ? '歌手' : '专辑'; } };
        }
    },
    window: {
        SetInterval(callback, interval) {
            assert.equal(interval, 250);
            const id = ++nextTimer;
            timers.set(id, callback);
            return id;
        },
        ClearInterval(id) { timers.delete(id); }
    },
    utils: {
        ReadTextFile(file, codepage) {
            assert.ok(file.endsWith('foobar-command.json'));
            assert.equal(codepage, 65001);
            if (throwReads) throw new Error('locked command file');
            return commandText;
        },
        WriteTextFile(file, text, bom) {
            if (throwWrites) throw new Error('locked file');
            if (failWrites) return false;
            assert.equal(bom, false);
            writes.push({ file, state: JSON.parse(text) });
            return true;
        }
    }
};
const context = vm.createContext(sandbox);
const load = () => vm.runInContext(source, context);
const tick = ms => { now += ms; [...timers.values()].forEach(callback => callback()); };
const current = () => writes.at(-1).state;

load();
assert.equal(timers.size, 1);
assert.equal(current().isPlaying, false);
assert.equal(current().path, '');
assert.equal(current().timestampUtcMs, now);
assert.equal(current().version, 1);
assert.equal(sandbox.on_playback_new_track, originalCallback);
assert.equal(sandbox.on_playback_pause, originalCallback);
assert.equal(commands.length, 0, 'existing startup command must be consumed without executing');

sandbox.fb.IsPlaying = true;
sandbox.fb.PlaybackTime = 1.25;
tick(250);
assert.equal(current().title, '歌名');
assert.equal(current().artist, '歌手');
assert.equal(current().album, '专辑');
assert.equal(current().path, handle.Path);
assert.equal(current().lyricsDirectory, 'C:\\Player\\profile\\lyrics');
assert.equal(current().embeddedLyrics, '[00:01.00]中文歌词');
assert.equal(current().positionSeconds, 1.25);
assert.equal(current().durationSeconds, 180);
sandbox.fb.PlaybackTime = 2;
tick(250);
assert.equal(current().positionSeconds, 2);

sandbox.fb.IsPaused = true;
tick(250);
assert.equal(current().isPaused, true);
assert.equal(current().isPlaying, true);
const pausedCount = writes.length;
tick(1750);
assert.equal(writes.length, pausedCount);
tick(250);
assert.equal(writes.length, pausedCount + 1);
sandbox.fb.PlaybackTime = 30;
tick(250);
assert.equal(current().positionSeconds, 30);

fields = { LYRICS: '[00:30.00]updated lyrics' };
tick(5000);
assert.equal(current().embeddedLyrics, '[00:30.00]updated lyrics');

failWrites = true;
tick(2500);
assert.equal(errors.length, 1);
tick(2500);
assert.equal(errors.length, 1);
failWrites = false;
throwWrites = true;
tick(250);
assert.equal(errors.length, 1);
throwWrites = false;
tick(250);
assert.equal(current().timestampUtcMs, now);

sandbox.fb.IsPlaying = false;
sandbox.fb.IsPaused = false;
tick(250);
assert.equal(current().title, '');
assert.equal(current().path, '');
assert.equal(current().positionSeconds, 0);
assert.equal(current().embeddedLyrics, '');

const command = (id, action, timestamp = now) => {
    commandText = JSON.stringify({ id, action, timestampUtcMs: timestamp });
    tick(250);
};
command('play-1', 'playpause');
assert.deepEqual(commands, ['playpause']);
tick(250);
assert.equal(commands.length, 1, 'one mailbox command must execute only once');
command('next-1', 'next');
command('previous-1', 'previous');
assert.deepEqual(commands, ['playpause', 'next', 'previous']);
command('play-1', 'playpause');
assert.equal(commands.length, 3, 'a previously consumed ID cannot be replayed after another ID');
command('stale', 'next', now - 3001);
command('future', 'next', now + 10000);
command('unknown', 'unexpected');
command('unknown', 'next');
command('old-session', 'next', 9999);
command('wrong-type', 'next', String(now));
command('i'.repeat(129), 'next');
assert.equal(commands.length, 3, 'invalid commands must not execute');
commandText = '{partial';
tick(250);
throwReads = true;
tick(250);
throwReads = false;
command('after-read-error', 'next');
assert.equal(commands.length, 4);
commandText = JSON.stringify({ id: 'oversized', action: 'next', timestampUtcMs: now, extra: 'x'.repeat(4096) });
tick(250);
assert.equal(commands.length, 4);
commandText = JSON.stringify({ id: 'fresh-at-reload', action: 'next', timestampUtcMs: now });
load();
assert.equal(timers.size, 1, 'duplicate include must replace its timer');
tick(250);
assert.equal(commands.length, 4, 'panel reload must consume an existing fresh command');
const queued = (id, action, timestampUtcMs = now) => ({ id, action, timestampUtcMs });
commandText = JSON.stringify({ version: 1, commands: [queued('rapid-1', 'next'), queued('rapid-2', 'next'), queued('rapid-3', 'previous')] });
tick(250);
assert.deepEqual(commands.slice(-3), ['next', 'next', 'previous'], 'rapid taps must execute in arrival order');
assert.equal(commands.length, 7);
tick(250);
assert.equal(commands.length, 7, 'an unchanged queue must not execute again');
commandText = JSON.stringify({ version: 1, commands: [queued('rapid-2', 'next'), queued('expired-queue', 'next', now - 4000), queued('rapid-4', 'playpause')] });
tick(250);
assert.equal(commands.length, 8, 'duplicates and expired items must not prevent the next valid tap');
assert.equal(commands.at(-1), 'playpause');
commandText = JSON.stringify({ version: 1, commands: Array.from({ length: 17 }, (_, i) => queued('oversized-' + i, 'next')) });
tick(250);
assert.equal(commands.length, 8, 'queue is bounded to 16 commands');
commandText = JSON.stringify({ version: 1, commands: [queued('reload-queue-1', 'next'), queued('reload-queue-2', 'next')] });
load();
tick(250);
assert.equal(commands.length, 8, 'panel reload must consume every existing queued command');
sandbox.TouchBarLyricsBridge.stop();
assert.equal(timers.size, 0);
console.log('FoobarBridge: all mocked playback, metadata, command deduplication/validation, error and reload checks passed.');
