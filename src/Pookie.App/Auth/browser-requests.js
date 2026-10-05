'use strict';

// Installed only in Pookie's own WebView. The website's tag handles its checks.
(() => {
  const siteOrigin = 'https://soundcloud.com';
  const apiOrigin = 'https://api-v2.soundcloud.com';
  if (location.origin !== siteOrigin || window !== window.top || window.__pookieBrowserRequests) return;
  window.__pookieBrowserRequests = true;
  const account = __POOKIE_ACCOUNT__;
  // Website storage belongs to the browser and the site's tag. A token exported
  // to .NET is NOT a cookie: restoring it loses expiry/domain/context and can
  // revive a rejected session. Never write it back into cookie/localStorage.
  const post = window.__pookiePost || (message => window.infiniframe.host.postData(message));
  const send = data => post({ id: 'pookie:web-api', version: 1, data });
  let lastProtection = '';
  function persistProtection() {
    try {
      const name = window.dataDomeOptions?.ddCookieSessionName || 'ddSession_datadome';
      const cookie = document.cookie.split(';').map(value => value.trim()).find(value => value.startsWith('datadome='));
      const value = window.localStorage.getItem(name) || (cookie ? decodeURIComponent(cookie.slice(9)) : '');
      if (/^[A-Za-z0-9._~+/%=-]{1,4096}$/.test(value) && value !== lastProtection) {
        lastProtection = value;
        send({ kind: 'protection-session', data_dome_client_id: value });
      }
    } catch { /* A storage failure must not interrupt website requests. */ }
  }
  // Rotation can happen without a visible challenge, including while the website is idle.
  let tagReady = false;
  let protectionReady = false;
  let readySent = false;
  let blocked = false;
  let releaseReady, rejectReady;
  const ready = new Promise((resolve, reject) => { releaseReady = resolve; rejectReady = reject; });
  ready.catch(() => {});
  let queue = Promise.resolve();
  const active = new Map();
  let challenge = null;
  let generation = 0;

  function checking(event) {
    if (blocked) return;
    const value = event.detail?.challengeType;
    const type = ['device_check', 'device_check_invisible_mode', 'block', 'hard_block'].includes(value) ? value : 'unknown';
    if (type === 'hard_block' || event.detail?.responseType === 'hardblock') {
      blocked = true;
      const error = new Error('site-blocked');
      const pending = challenge; challenge = null;
      pending?.reject(error);
      rejectReady(error);
      send({ kind: 'blocked', interactive: true, challenge_type: 'hard_block' });
      return;
    }
    if (!challenge) {
      let resolve, reject;
      const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
      promise.catch(() => {});
      challenge = { promise, resolve, reject };
    }
    send({ kind: 'checking', interactive: type !== 'device_check' && type !== 'device_check_invisible_mode', challenge_type: type });
  }
  window.addEventListener('dd_ready', () => { tagReady = true; announceReady(); });
  window.addEventListener('dd_post_done', () => { protectionReady = true; persistProtection(); announceReady(); });
  window.addEventListener('load', announceReady);
  window.addEventListener('dd_blocked', checking);
  window.addEventListener('dd_response_displayed', checking);
  window.addEventListener('dd_response_passed', () => {
    if (blocked) return;
    protectionReady = true;
    generation++;
    const pending = challenge; challenge = null;
    pending?.resolve();
    persistProtection();
    setTimeout(persistProtection, 0);
    send({ kind: 'passed' });
    announceReady();
  });
  window.addEventListener('dd_response_error', () => {
    if (blocked) return;
    const pending = challenge; challenge = null;
    pending?.reject(new Error('challenge-error'));
    send({ kind: 'challenge-error' });
  });
  window.addEventListener('dd_response_unload', () => {
    if (blocked) return;
    if (!challenge) return;
    const pending = challenge; challenge = null;
    pending.reject(new Error('challenge-closed'));
    send({ kind: 'challenge-error' });
  });

  function hasBrowserSession() {
    try {
      return Boolean(window.localStorage.getItem(window.dataDomeOptions?.ddCookieSessionName || 'ddSession_datadome') ||
        document.cookie.split(';').some(value => value.trim().startsWith('datadome=')));
    } catch { return document.cookie.includes('datadome='); }
  }
  function announceReady() {
    // dd_ready only means the tag loaded, not that its initial payload returned.
    // For late injection, resource timing can witness the site's completed POST.
    if (!protectionReady) {
      try {
        const endpoint = new URL(window.dataDomeOptions?.endpoint || window.ddoptions?.endpoint || 'https://dwt.soundcloud.com/js/');
        protectionReady = window.performance?.getEntriesByType('resource').some(entry =>
          ['xmlhttprequest', 'fetch'].includes(entry.initiatorType) && entry.responseEnd > 0 &&
          (!entry.responseStatus || entry.responseStatus >= 200 && entry.responseStatus < 300) &&
          new URL(entry.name).origin === endpoint.origin && new URL(entry.name).pathname === endpoint.pathname) || false;
      } catch { }
    }
    if (readySent || blocked || challenge || !protectionReady || document.readyState !== 'complete' || !hasBrowserSession() ||
      !(tagReady || window.ddSbh && window.dataDomeOptions)) return;
    readySent = true;
    persistProtection();
    releaseReady();
    send({ kind: 'ready' });
  }
  announceReady();
  const readyTimer = setInterval(() => { announceReady(); if (readySent) clearInterval(readyTimer); }, 250);
  setInterval(persistProtection, 500);

  function wait(promise, context) { return Promise.race([promise, context.cancelled]); }
  function checkCancelled(context) { if (context.controller.signal.aborted) throw new Error('cancelled'); }

  async function fetchProtected(url, method, context) {
    checkCancelled(context);
    await wait(ready, context);
    if (blocked) throw new Error('site-blocked');
    if (challenge) await wait(challenge.promise, context);
    url.searchParams.set('client_id', account.client_id);
    const version = window.__sc_version || account.app_version;
    if (version) url.searchParams.set('app_version', version);
    const locale = document.documentElement.lang || account.app_locale;
    if (locale) url.searchParams.set('app_locale', locale);
    const initialGeneration = generation;
    for (let attempt = 0; attempt < 2; attempt++) {
      checkCancelled(context);
      if (blocked) throw new Error('site-blocked');
      let response;
      try {
        // Use the live fetch intercepted by the website's DataDome tag, not .NET HTTP or a saved cookie.
        response = await wait(window.fetch(url.href, {
          method,
          headers: { Authorization: `OAuth ${account.access_token}`, Accept: 'application/json, text/javascript, */*; q=0.1' },
          redirect: 'error', signal: context.controller.signal
        }), context);
      } catch (error) {
        checkCancelled(context);
        if (blocked) throw new Error('site-blocked');
        // The tag can abort Fetch while its challenge remains active.
        if (attempt === 0 && (challenge || generation !== initialGeneration)) {
          if (challenge) await wait(challenge.promise, context);
          continue;
        }
        throw error;
      }
      if (blocked) throw new Error('site-blocked');
      if (response.status === 403 && attempt === 0 && (challenge || generation !== initialGeneration)) {
        if (challenge) await wait(challenge.promise, context);
        continue;
      }
      persistProtection();
      return response;
    }
  }

  function readUrl(value) {
    try {
      if (typeof value !== 'string' || value.length > 8192) return null;
      const url = new URL(value);
      // Check the real API address before remapping local smoke-test fixtures.
      if (url.origin !== 'https://api-v2.soundcloud.com' || url.username || url.password || url.hash ||
        !(/^\/system-playlists\/soundcloud(%3A|:)system-playlists(%3A|:)[A-Za-z0-9%:_-]+$/i.test(url.pathname) || /^\/(me|me\/track_likes\/ids|search\/tracks|stream|resolve|tracks|me\/library\/(all|stations)|me\/play-history\/(contexts|tracks))$/.test(url.pathname) ||
          /^\/media\//.test(url.pathname) || /^\/((tracks|playlists)\/[1-9][0-9]*|users\/[1-9][0-9]*\/(likes|followings|tracks))$/.test(url.pathname))) return null;
      return new URL(url.pathname + url.search, apiOrigin);
    } catch { return null; }
  }

  async function readJson(response, context) {
    const maxBytes = 2 * 1024 * 1024;
    let text;
    if (response.body?.getReader) {
      const reader = response.body.getReader(), decoder = new TextDecoder();
      let bytes = 0;
      const parts = [];
      try {
        while (true) {
          const { done, value } = await wait(reader.read(), context);
          if (done) break;
          bytes += value.byteLength;
          if (bytes > maxBytes) throw new Error('response-too-large');
          parts.push(decoder.decode(value, { stream: true }));
        }
        parts.push(decoder.decode());
        text = parts.join('');
      } catch (error) { await reader.cancel().catch(() => {}); throw error; }
      finally { reader.releaseLock(); }
    } else {
      text = await wait(response.text(), context);
      if (new TextEncoder().encode(text).byteLength > maxBytes) throw new Error('response-too-large');
    }
    checkCancelled(context);
    return { text, json: JSON.parse(text) };
  }

  async function request(command, context) {
    checkCancelled(context);
    if (command.operation === 'api-get') {
      const response = await fetchProtected(readUrl(command.url), 'GET', context);
      if (response.ok) {
        const { text } = await readJson(response, context);
        let index = 0;
        for (let start = 0; start < text.length;) {
          let end = Math.min(start + 1024, text.length);
          // Don't split an astral character across IPC JSON strings.
          if (end < text.length && /[\uD800-\uDBFF]/.test(text[end - 1])) end--;
          send({ kind: 'json-chunk', request_id: command.id, chunk_index: index++, chunk: text.slice(start, end) });
          start = end;
        }
      }
      send({ kind: 'complete', request_id: command.id, status: response.status });
      return;
    }
    const url = new URL(command.operation === 'me' ? '/me' : command.operation === 'liked-ids' ?
      '/me/track_likes/ids?limit=200&linked_partitioning=1' : `/users/${command.user_id}/track_likes/${command.track_id}`, apiOrigin);
    if (command.operation === 'liked-ids') {
      let target = url, count = 0, pages = 0;
      const seen = new Set();
      while (target) {
        if (++pages > 1000 || target.origin !== apiOrigin || target.pathname !== '/me/track_likes/ids' || target.username || target.password || seen.has(target.href))
          throw new Error('invalid-pagination');
        seen.add(target.href);
        const response = await fetchProtected(target, 'GET', context);
        if (!response.ok) { send({ kind:'complete', request_id:command.id, status:response.status }); return; }
        const { json: body } = await readJson(response, context);
        const values = Array.isArray(body) ? body : body.collection;
        if (!Array.isArray(values)) throw new Error('invalid-ids');
        let batch = [];
        for (const value of values) {
          const id = typeof value === 'number' ? value : typeof value === 'string' && /^\d+$/.test(value) ? Number(value) : 0;
          if (!Number.isSafeInteger(id) || id <= 0) continue;
          if (++count > 100000) throw new Error('too-many-ids');
          batch.push(id);
          if (batch.length === 200) { send({ kind:'ids', request_id:command.id, ids:batch }); batch = []; }
        }
        if (batch.length) send({ kind:'ids', request_id:command.id, ids:batch });
        target = !Array.isArray(body) && body.next_href ? new URL(body.next_href) : null;
      }
      send({ kind:'complete', request_id:command.id, status:200 });
    } else {
      const response = await fetchProtected(url, command.operation === 'me' ? 'GET' : command.liked ? 'PUT' : 'DELETE', context);
      const result = { kind: 'complete', request_id: command.id, status: response.status };
      if (command.operation === 'me' && response.ok) {
        const { json: user } = await readJson(response, context);
        result.user = { id: user.id, username: user.username, avatar_url: user.avatar_url };
      }
      send(result);
    }
  }

  window.__pookieRequest = command => {
    if (!command || !/^[a-f0-9]{32}$/.test(command.id || '')) return;
    if (command.operation === 'cancel') {
      const context = active.get(command.id);
      // Writes are cancelled by terminating the worker, never replayed later.
      if (context && context.operation !== 'like') {
        context.controller.abort(); context.rejectCancel(new Error('cancelled'));
      }
      return;
    }
    if (active.has(command.id) ||
      !(command.operation === 'api-get' && readUrl(command.url) ||
        command.operation === 'me' || command.operation === 'liked-ids' || command.operation === 'like' &&
        Number.isSafeInteger(command.user_id) && command.user_id > 0 && Number.isSafeInteger(command.track_id) && command.track_id > 0 &&
        typeof command.liked === 'boolean')) return;
    let rejectCancel;
    const cancelled = new Promise((_, reject) => { rejectCancel = reject; });
    cancelled.catch(() => {});
    const context = { operation: command.operation, controller: new AbortController(), cancelled, rejectCancel };
    active.set(command.id, context);
    // Preserve order even when a check interrupts a request or arrives between requests.
    queue = queue.then(() => request(command, context)).catch(() =>
      send({ kind: 'complete', request_id: command.id, status: blocked ? 403 : 0 }))
      .finally(() => active.delete(command.id));
  };
})();
