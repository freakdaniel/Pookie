'use strict';

// Pookie's own encrypted audio element. No website player, page audio, ad capture,
// license export or content-key extraction. The browser owns the EME session.
(() => {
  if (location.origin !== 'https://soundcloud.com' || window !== window.top || window.__pookieAudioRequest) return;
  const mime = 'audio/mp4; codecs="mp4a.40.2"';
  let current = null;
  const pendingSeeks = new Map();
  // Autoplay is enabled for app-controlled playback; keep the hidden website silent.
  document.addEventListener('play', event => {
    if (event.target instanceof HTMLMediaElement && event.target !== current?.audio) event.target.pause();
  }, true);
  for (const media of document.querySelectorAll('audio,video')) media.pause();
  function mediaUrl(value, base) {
    const url = new URL(value, base);
    if (url.protocol !== 'https:' || url.port && url.port !== '443' || url.username || url.password || url.hash ||
        !(url.hostname === 'sndcdn.com' || url.hostname.endsWith('.sndcdn.com') ||
          url.hostname === 'playback.media-streaming.soundcloud.cloud')) throw new Error('playlist');
    return url.href;
  }
  function valid(c) {
    return c && /^[a-f0-9]{32}$/.test(c.playback_id || '') && Number.isFinite(c.position) && c.position >= 0 && c.position <= 86400 &&
      Number.isFinite(c.volume) && c.volume >= 0 && c.volume <= 100 && (c.action === 'start'
      ? typeof c.source === 'string' && c.source.length <= 8192 && typeof c.authorization === 'string' &&
        c.authorization.length > 0 && c.authorization.length <= 16384 && !/[\x00-\x1f\x7f]/.test(c.authorization)
      : ['stop','pause','volume','seek'].includes(c.action) && c.source == null && c.authorization == null);
  }
  function state(st) {
    let bufferedStart = 0, bufferedEnd = 0;
    const ranges = st.audio.buffered;
    for (let i = 0; i < ranges.length; i++) {
      if (ranges.start(i) <= st.audio.currentTime && ranges.end(i) >= st.audio.currentTime) {
        bufferedStart = Math.min(st.duration || 0, Math.max(0, ranges.start(i)));
        bufferedEnd = Math.min(st.duration || 0, Math.max(bufferedStart, ranges.end(i)));
        break;
      }
    }
    return {position: Math.max(0, st.audio.currentTime || 0), duration: st.duration || 0,
      buffered_start:bufferedStart, buffered_end:bufferedEnd,
      playing: !st.paused && !st.audio.paused && !st.audio.ended && st.audio.readyState >= 3 && !st.error,
      buffering: !st.paused && !st.audio.ended && st.audio.readyState < 3 && !st.error,
      ended: st.audio.ended, error: st.error, stage:st.stage,
      media_error:st.audio.error?.code || 0, ready_state:st.audio.readyState, network_state:st.audio.networkState};
  }
  function publish(st) {
    if (current === st) st.send({kind:'audio-state', request_id:st.id, status:200, audio:state(st)});
  }
  function fail(st, error) {
    if (current !== st || st.controller.signal.aborted || st.error) return;
    st.error = ['unsupported','network','playlist','decode','license','expired','autoplay'].includes(error.message) ? error.message : 'decode';
    clearInterval(st.timer);
    st.audio.pause(); publish(st);
    st.rejectKeys(error); st.rejectReady(error);
    st.controller.abort();
  }
  function stop(st) {
    if (!st) return;
    if (current === st) current = null;
    clearInterval(st.timer); st.controller.abort(); st.audio.pause();
    st.audio.removeAttribute('src'); st.audio.load(); st.audio.remove();
    if (st.objectUrl) URL.revokeObjectURL(st.objectUrl);
    if (st.session) st.session.close().catch(() => { /* CDM may already be closed during teardown. */ });
    const error = new Error('closed'); st.rejectKeys(error); st.rejectReady(error);
  }
  async function bytes(url, limit, st) {
    if (st.controller.signal.aborted) throw new Error('closed');
    const timeout = new AbortController();
    const abort = () => timeout.abort();
    st.controller.signal.addEventListener('abort', abort, {once:true});
    const timer = setTimeout(abort, 30000);
    try {
      const response = await fetch(mediaUrl(url), {signal:timeout.signal, redirect:'error', credentials:'omit'});
      if (!response.ok || !response.body) throw new Error('network');
      const reader = response.body.getReader(), parts = []; let length = 0;
      try {
        while (true) {
          const item = await reader.read(); if (item.done) break;
          length += item.value.length;
          if (length > limit) { await reader.cancel(); throw new Error('network'); }
          parts.push(item.value);
        }
      } finally { reader.releaseLock(); }
      const output = new Uint8Array(length); let offset = 0;
      for (const part of parts) { output.set(part, offset); offset += part.length; }
      return output;
    } finally { clearTimeout(timer); st.controller.signal.removeEventListener('abort', abort); }
  }
  function attributes(text) {
    const result = new Map();
    const pattern = /([A-Z0-9-]+)=(?:"([^"]*)"|([^,]*))(?:,|$)/g;
    let match, end = 0;
    while ((match = pattern.exec(text))) {
      if (match.index !== end || result.has(match[1])) throw new Error('playlist');
      result.set(match[1], match[2] ?? match[3]); end = pattern.lastIndex;
    }
    if (end !== text.length) throw new Error('playlist');
    return result;
  }
  function playlist(text, origin) {
    if (!text.trimStart().startsWith('#EXTM3U')) throw new Error('playlist');
    let init = null, pssh = null, duration = 0, start = 0, complete = false; const segments = [];
    for (let line of text.split('\n')) {
      line = line.trim();
      if (/^#EXT-X-(STREAM-INF|BYTERANGE|DISCONTINUITY)(:|$)/.test(line)) throw new Error('playlist');
      if (line.startsWith('#EXT-X-MAP:')) {
        const a = attributes(line.slice(11)); if (a.has('BYTERANGE') || !a.get('URI')) throw new Error('playlist');
        const value = mediaUrl(a.get('URI'), origin); if (init && init !== value) throw new Error('playlist'); init = value;
      } else if (line.startsWith('#EXT-X-KEY:')) {
        const a = attributes(line.slice(11));
        if (a.get('KEYFORMAT') !== 'urn:uuid:edef8ba9-79d6-4ace-a3c8-27dcd51d21ed') continue;
        const value = a.get('URI');
        if (!['SAMPLE-AES','SAMPLE-AES-CTR'].includes(a.get('METHOD')) || !value || value.length > 90000 ||
            !/^data:[^,]*;base64,/.test(value) || pssh && pssh !== value) throw new Error('playlist');
        pssh = value;
      } else if (line.startsWith('#EXTINF:')) {
        duration = Number(line.slice(8).split(',')[0]);
        if (!Number.isFinite(duration) || duration <= 0 || duration > 300) throw new Error('playlist');
      } else if (line === '#EXT-X-ENDLIST') complete = true;
      else if (line && !line.startsWith('#')) {
        if (!duration || segments.length >= 10000) throw new Error('playlist');
        segments.push({url:mediaUrl(line, origin), start, duration}); start += duration; duration = 0;
      }
    }
    if (!complete || !init || !pssh || !segments.length || start > 86400) throw new Error('playlist');
    const initData = Uint8Array.from(atob(pssh.slice(pssh.indexOf(',') + 1)), c => c.charCodeAt(0));
    if (initData.length < 32 || initData.length > 65536) throw new Error('playlist');
    return {init, initData, segments, duration:start};
  }
  function event(target, success, action, st) {
    if (st.controller.signal.aborted) return Promise.reject(new Error('closed'));
    return new Promise((resolve, reject) => {
      const cleanup = () => { clearTimeout(timer); target.removeEventListener(success, yes); target.removeEventListener('error', no);
        st.controller.signal.removeEventListener('abort', no); };
      const yes = () => { cleanup(); resolve(); };
      const no = () => { cleanup(); reject(new Error('decode')); };
      const timer = setTimeout(no, 30000);
      target.addEventListener(success, yes, {once:true}); target.addEventListener('error', no, {once:true});
      st.controller.signal.addEventListener('abort', no, {once:true});
      try { action(); } catch (error) { cleanup(); reject(error); }
    });
  }
  function license(st, challenge, authorization) {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      const abort = () => { xhr.abort(); reject(new Error('closed')); };
      st.controller.signal.addEventListener('abort', abort, {once:true});
      const finish = () => st.controller.signal.removeEventListener('abort', abort);
      xhr.open('POST','https://license.media-streaming.soundcloud.cloud/playback/widevine?license_token=' + encodeURIComponent(authorization));
      xhr.responseType = 'arraybuffer'; xhr.timeout = 20000;
      xhr.setRequestHeader('Content-Type','application/octet-stream'); xhr.setRequestHeader('X-SC-Application-Id','46941');
      xhr.onerror = xhr.ontimeout = () => { finish(); reject(new Error('network')); };
      xhr.onprogress = e => { if (e.loaded > 65536) { finish(); xhr.abort(); reject(new Error('license')); } };
      xhr.onload = () => {
        finish();
        if (xhr.status < 200 || xhr.status >= 300 || !xhr.response || xhr.response.byteLength > 65536) reject(new Error('license'));
        else resolve(xhr.response);
      };
      xhr.send(challenge);
    });
  }
  async function prepare(st, command) {
    if (!window.MediaSource || !MediaSource.isTypeSupported(mime) || !navigator.requestMediaKeySystemAccess) throw new Error('unsupported');
    const p = st.playlist;
    st.playlist = p; st.duration = p.duration;
    st.stage = 'eme';
    const access = await navigator.requestMediaKeySystemAccess('com.widevine.alpha', [{initDataTypes:['cenc'],
      audioCapabilities:[{contentType:mime}], distinctiveIdentifier:'not-allowed', persistentState:'not-allowed'}]);
    if (current !== st || st.controller.signal.aborted) throw new Error('closed');
    const keys = await access.createMediaKeys();
    if (current !== st || st.controller.signal.aborted) throw new Error('closed');
    await st.audio.setMediaKeys(keys);
    if (current !== st || st.controller.signal.aborted) throw new Error('closed');
    const session = st.session = keys.createSession('temporary');
    session.addEventListener('keystatuseschange', () => {
      const statuses = Array.from(session.keyStatuses.values());
      if (statuses.includes('usable')) st.resolveKeys();
      else if (statuses.some(value => !['status-pending','usable-in-future'].includes(value))) fail(st, new Error('expired'));
    });
    let exchangeQueue = Promise.resolve(), exchanges = 0;
    session.addEventListener('message', e => {
      if (!['license-request','license-renewal'].includes(e.messageType) || ++exchanges > 64) { fail(st, new Error('license')); return; }
      exchangeQueue = exchangeQueue.then(async () => {
        const response = await license(st, e.message, command.authorization);
        if (current !== st) return;
        await session.update(response);
      }).catch(error => fail(st, error));
    });
    const licenseTimer = setTimeout(() => fail(st, new Error('license')), 45000);
    st.stage = 'license';
    try { await session.generateRequest('cenc', p.initData); await st.keys; }
    finally { clearTimeout(licenseTimer); }
    if (current !== st || st.controller.signal.aborted) throw new Error('closed');
    const source = st.mediaSource = new MediaSource(); st.objectUrl = URL.createObjectURL(source);
    st.stage = 'source';
    await event(source, 'sourceopen', () => {
      st.audio.src = st.objectUrl; st.audio.load();
      if (!st.paused) {
        st.startPlay = st.audio.play();
        st.startPlay.catch(error => { if (error.name === 'NotAllowedError') fail(st, new Error('autoplay')); });
      }
    }, st);
    source.duration = st.duration;
    st.buffer = source.addSourceBuffer(mime);
    st.stage = 'buffer';
    await event(st.buffer, 'updateend', () => st.buffer.appendBuffer(st.initialization), st);
    await appendNext(st);
    st.stage = 'play';
    if (!st.paused) {
      try { await (st.startPlay || st.audio.play()); } catch (error) { throw new Error(error.name === 'NotAllowedError' ? 'autoplay' : 'decode'); }
    }
    st.prepared = true; st.stage = 'ready'; st.resolveReady(); publish(st); pump(st);
  }
  async function appendNext(st) {
    if (st.index >= st.playlist.segments.length || st.mediaSource.readyState !== 'open') return;
    const data = await bytes(st.playlist.segments[st.index].url, 16 * 1024 * 1024, st);
    if (current !== st) return;
    await event(st.buffer, 'updateend', () => st.buffer.appendBuffer(data), st); st.index++;
    if (st.index === st.playlist.segments.length && st.mediaSource.readyState === 'open') st.mediaSource.endOfStream();
  }
  function serialize(st, operation) {
    const result = st.operations.then(() => { if (current !== st) throw new Error('closed'); return operation(); });
    st.operations = result.catch(error => { if (error.message !== 'cancelled') fail(st, error); }); return result;
  }
  function pump(st) {
    if (st.pumping || !st.prepared || !st.buffer || st.error || current !== st) return;
    st.pumping = true;
    serialize(st, async () => {
      if (current === st && st.index < st.playlist.segments.length &&
        st.playlist.segments[st.index].start < st.audio.currentTime + 45) await appendNext(st);
      if (st.mediaSource.readyState === 'open' && st.audio.currentTime > 30 && st.buffer.buffered.length && st.buffer.buffered.start(0) < st.audio.currentTime - 20)
        await event(st.buffer, 'updateend', () => st.buffer.remove(0, st.audio.currentTime - 20), st);
    }).catch(error => fail(st, error)).finally(() => { st.pumping = false; });
  }
  async function seek(st, position, revision) {
    const check = () => { if (revision !== st.seekRevision) throw new Error('cancelled'); };
    await st.ready;
    await serialize(st, async () => {
      check();
      const target = Math.max(0, Math.min(position, st.duration - .01));
      let buffered = false;
      for (let i = 0; i < st.buffer.buffered.length; i++)
        if (st.buffer.buffered.start(i) <= target && st.buffer.buffered.end(i) > target + .1) buffered = true;
      if (!buffered) {
        if (st.buffer.buffered.length) await event(st.buffer, 'updateend', () => st.buffer.remove(0, st.duration + 1), st);
        check();
        st.index = Math.max(0, st.playlist.segments.findIndex(segment => segment.start + segment.duration > target) - 1);
        await appendNext(st); check(); await appendNext(st); check();
      }
      check();
      st.audio.currentTime = target;
      // play() may wait for more buffered data. Release the SourceBuffer queue
      // first so the pump and a newer seek can supply it instead of deadlocking.
      if (!st.paused) st.audio.play().catch(error => fail(st, new Error(error.name === 'NotAllowedError' ? 'autoplay' : 'decode')));
    });
    publish(st); pump(st);
  }
  window.__pookieAudioCancel = id => {
    if (current?.requestId === id) stop(current);
    pendingSeeks.get(id)?.();
  };
  window.__pookieAudioRequest = async (request, send) => {
    const c = request.audio;
    if (!valid(c)) { send({kind:'complete', request_id:request.id, status:400}); return; }
    let st;
    try {
      if (c.action === 'start') {
        mediaUrl(c.source); stop(current);
        const audio = document.createElement('audio'); audio.preload = 'auto'; audio.volume = c.volume / 100;
        audio.style.display = 'none'; document.body.append(audio);
        st = {id:c.playback_id, requestId:request.id, audio, send, paused:c.paused, duration:0, index:0,
          controller:new AbortController(), operations:Promise.resolve(), error:null, stage:'manifest', seekRevision:0};
        st.keys = new Promise((resolve, reject) => { st.resolveKeys = resolve; st.rejectKeys = reject; }); st.keys.catch(() => {});
        st.ready = new Promise((resolve, reject) => { st.resolveReady = resolve; st.rejectReady = reject; }); st.ready.catch(() => {});
        current = st;
        audio.addEventListener('error', () => fail(st, new Error('decode')));
        for (const name of ['playing','pause','waiting','ended','seeked']) audio.addEventListener(name, () => publish(st));
        st.timer = setInterval(() => { publish(st); pump(st); }, 250);
        // Initialization is fetched with the same bounded media-only transport as segments.
        const manifest = playlist(new TextDecoder().decode(await bytes(c.source, 1024 * 1024, st)), c.source);
        st.playlist = manifest;
        st.stage = 'initialization';
        st.initialization = await bytes(manifest.init, 1024 * 1024, st);
        await prepare(st, c);
      } else {
        st = current;
        if (!st || st.id !== c.playback_id) { send({kind:'complete', request_id:request.id, status:200}); return; }
        if (c.action === 'stop') stop(st);
        else if (c.action === 'volume') st.audio.volume = c.volume / 100;
        else if (c.action === 'pause') { st.paused = c.paused; if (c.paused) st.audio.pause(); else if (st.buffer) await st.audio.play(); }
        else if (c.action === 'seek') {
          const revision = ++st.seekRevision;
          pendingSeeks.set(request.id, () => { if (st.seekRevision === revision) st.seekRevision++; });
          await seek(st, c.position, revision);
        }
        publish(st);
      }
      send({kind:'complete', request_id:request.id, status:200});
    } catch (error) {
      if (st && error.message !== 'cancelled') fail(st, error);
      send({kind:'complete', request_id:request.id, status:error.message === 'cancelled' ? 200 : 0, audio:st ? state(st) : null});
    } finally { pendingSeeks.delete(request.id); }
  };
  window.addEventListener('pagehide', () => {
    if (current) { current.error = 'closed'; publish(current); stop(current); }
  }, {once:true});
})();
