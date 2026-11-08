// Touch Bar playback-state bridge for foobar2000 JSplitter / Spider Monkey Panel.
// Include once from an always-loaded panel such as bottombar.js.
// Exports playback state and accepts only previous/playpause/next commands for this player.
// The containing folder must already exist; change this one path when moving TouchBar-USBIP.
if (typeof TouchBarLyricsBridge !== 'undefined' && TouchBarLyricsBridge &&
    typeof TouchBarLyricsBridge.stop === 'function') {
    TouchBarLyricsBridge.stop();
}

var TouchBarLyricsBridge = (function () {
    'use strict';

    var configuredDirectory = '';
    if (typeof TouchBarBridgeOutputPath === 'string' && TouchBarBridgeOutputPath.length > 0) {
        configuredDirectory = TouchBarBridgeOutputPath;
    } else if (typeof utils.GetEnv === 'function') {
        configuredDirectory = utils.GetEnv('TOUCHBAR_APP_DIR') || '';
    }
    var outputPath = configuredDirectory.length > 0
        ? configuredDirectory.replace(/[\\\\/]$/, '') + '\\foobar-state.json'
        : 'TouchBar-USBIP\\app\\foobar-state.json';
    var commandPath = outputPath.replace(/foobar-state\.json$/, 'foobar-command.json');
    var startedUtcMs = Date.now();
    var consumedCommandIds = Object.create(null);
    var timer = null;
    var lastWriteMs = 0;
    var lastErrorMs = 0;
    var metadataReadMs = 0;
    var metadataKey = null;
    var lastStatus = null;
    var metadata = emptyMetadata();
    var titleFormat = fb.TitleFormat('$if2(%title%,%filename%)');
    var artistFormat = fb.TitleFormat('$if2(%artist%,)');
    var albumFormat = fb.TitleFormat('$if2(%album%,)');

    function emptyMetadata() {
        return { title: '', artist: '', album: '', path: '', embeddedLyrics: '' };
    }

    function safeText(value, maxLength) {
        return value == null ? '' : String(value).slice(0, maxLength || 8192);
    }

    function seconds(value) {
        value = Number(value);
        return isFinite(value) && value >= 0 ? value : 0;
    }

    function tagValue(info, name) {
        var index = info.MetaFind(name);
        if (index < 0) return '';
        var values = [];
        for (var i = 0; i < info.MetaValueCount(index); i++) {
            values.push(safeText(info.MetaValue(index, i), 512 * 1024));
        }
        return values.join('\n').slice(0, 512 * 1024);
    }

    function readMetadata(handle) {
        var result = emptyMetadata();
        if (!handle) return result;
        result.path = safeText(handle.Path);
        result.title = safeText(titleFormat.EvalWithMetadb(handle));
        result.artist = safeText(artistFormat.EvalWithMetadb(handle));
        result.album = safeText(albumFormat.EvalWithMetadb(handle));
        var info = handle.GetFileInfo();
        if (info) {
            var fields = ['SYNCEDLYRICS', 'LYRICS', 'UNSYNCEDLYRICS'];
            for (var i = 0; i < fields.length; i++) {
                var lyrics = tagValue(info, fields[i]);
                if (!lyrics) continue;
                if (!result.embeddedLyrics) result.embeddedLyrics = lyrics;
                // Prefer timestamped text if an earlier field contained plain lyrics.
                if (/\[\d{1,3}:\d{2}(?:[.:]\d{1,3})?\]/.test(lyrics)) {
                    result.embeddedLyrics = lyrics;
                    break;
                }
            }
        }
        return result;
    }

    function consumeCommand(command, consumeOnly) {
        if (!command || typeof command.id !== 'string' || !command.id || command.id.length > 128) return;
        if (Object.prototype.hasOwnProperty.call(consumedCommandIds, command.id)) return;
        // Consume rejected IDs too: changing the contents cannot re-arm the same command.
        consumedCommandIds[command.id] = true;
        if (consumeOnly || typeof command.timestampUtcMs !== 'number' ||
            !isFinite(command.timestampUtcMs) || command.timestampUtcMs < startedUtcMs) return;
        var age = Date.now() - command.timestampUtcMs;
        if (age < 0 || age > 3000) return;
        try {
            switch (command.action) {
                case 'previous': fb.Prev(); break;
                case 'playpause': fb.PlayOrPause(); break;
                case 'next': fb.Next(); break;
            }
        } catch (error) {
            // One failed playback action must not discard subsequent queued taps.
        }
    }

    function pollCommand(consumeOnly) {
        // A missing or partly written file must not interrupt state export or the panel.
        try {
            var text = utils.ReadTextFile(commandPath, 65001);
            if (!text || text.length > 4096) return;
            var mailbox = JSON.parse(text.replace(/^\uFEFF/, ''));
            if (mailbox && Array.isArray(mailbox.commands)) {
                if (mailbox.version !== 1 || mailbox.commands.length > 16) return;
                // Preserve tap order even when multiple presses arrive between polls.
                mailbox.commands.forEach(function (command) { consumeCommand(command, consumeOnly); });
            } else consumeCommand(mailbox, consumeOnly);
        } catch (error) {
            // Includes parse/read failures and playback errors; never retry an executed ID.
        }
    }

    function tick() {
        var now = Date.now();
        pollCommand(false);
        try {
            var playing = !!fb.IsPlaying;
            var paused = !!fb.IsPaused;
            var handle = (playing || paused) ? fb.GetNowPlaying() : null;
            var key = handle ? safeText(handle.RawPath || handle.Path) + '#' + handle.SubSong : '';
            var changedTrack = key !== metadataKey;
            if (changedTrack || now - metadataReadMs >= 5000) {
                metadata = readMetadata(handle);
                metadataKey = key;
                metadataReadMs = now;
            }

            var position = handle ? seconds(fb.PlaybackTime) : 0;
            var status = [key, playing, paused, position].join('|');
            // Keep the receiver alive while paused/stopped, and report seeks immediately.
            if (!changedTrack && status === lastStatus && now - lastWriteMs < 2000) return;

            var snapshot = {
                version: 1,
                timestampUtcMs: now,
                title: metadata.title,
                artist: metadata.artist,
                album: metadata.album,
                path: metadata.path,
                positionSeconds: position,
                durationSeconds: handle ? seconds(fb.PlaybackLength) : 0,
                isPlaying: playing,
                isPaused: paused,
                lyricsDirectory: fb.ProfilePath + 'lyrics',
                embeddedLyrics: metadata.embeddedLyrics
            };
            if (!utils.WriteTextFile(outputPath, JSON.stringify(snapshot), false)) {
                throw new Error('Could not write ' + outputPath);
            }
            lastWriteMs = now;
            lastStatus = status;
        } catch (error) {
            // An unavailable output folder must never break foobox's playback controls.
            if (!lastErrorMs || now - lastErrorMs >= 60000) {
                console.log('TouchBarLyricsBridge: ' + error);
                lastErrorMs = now;
            }
        }
    }

    // Consume any mailbox already present on panel load; never replay across a restart.
    pollCommand(true);
    tick();
    timer = window.SetInterval(tick, 250);
    return {
        outputPath: outputPath,
        commandPath: commandPath,
        stop: function () {
            if (timer !== null) {
                window.ClearInterval(timer);
                timer = null;
            }
        }
    };
})();
