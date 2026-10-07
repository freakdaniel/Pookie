const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.join(__dirname, '../src/Pookie.App/Browser/Scripts/login-capture.js'), 'utf8');

function harness(origin = 'https://soundcloud.com', cookie = '', hydration = []) {
  const messages = [], calls = [], timers = [], events = new Map();
  const window = {
    addEventListener: (name, handler) => events.set(name, handler),
    __sc_hydration: hydration,
    infiniframe: { host: { postData: value => messages.push(value) } },
    fetch: (...args) => { calls.push(args); return Promise.resolve('response'); }
  };
  window.top = window;
  function XHR() {}
  XHR.prototype.open = function(...args) { this.openArgs = args; };
  XHR.prototype.setRequestHeader = function(...args) { this.headerArgs = args; };
  XHR.prototype.send = function(body) { this.body = body; };
  const context = vm.createContext({ window, document: { cookie }, location: { origin, href: origin + '/' },
    navigator: { userAgent: 'WebKit' }, URL, Headers, Request, XMLHttpRequest: XHR,
    setInterval: callback => timers.push(callback), clearInterval: () => {} });
  vm.runInContext(script, context);
  return { window, messages, calls, XHR, context, timers, events };
}

test('login observer preserves fetch and transfers only one complete session', async () => {
  const h = harness();
  const options = { headers: { Authorization: 'OAuth fake-token' } };
  assert.equal(await h.window.fetch('https://api-v2.soundcloud.com/me', options), 'response');
  assert.equal(h.messages.length, 0);
  await h.window.fetch('https://api-v2.soundcloud.com/search?client_id=publicid');
  assert.equal(h.messages.length, 1);
  assert.equal(h.messages[0].data.access_token, 'fake-token');
  assert.equal(h.messages[0].data.client_id, 'publicid');
  assert.equal(h.calls[0][1], options);
  await h.window.fetch('https://api-v2.soundcloud.com/me?client_id=other', options);
  assert.equal(h.messages.length, 1);
});

test('login observer ignores foreign origins and API lookalikes', async () => {
  const h = harness();
  const options = { headers: { Authorization: 'OAuth fake-token' } };
  for (const url of ['http://api-v2.soundcloud.com/me', 'https://api-v2.soundcloud.com.evil.test/me',
    'https://api-v2.soundcloud.com:8443/me', 'https://user@api-v2.soundcloud.com/me']) {
    await h.window.fetch(url + '?client_id=id', options);
  }
  assert.equal(h.messages.length, 0);
  const foreign = harness('https://evil.test');
  const original = foreign.window.fetch;
  vm.runInContext(script, foreign.context);
  assert.equal(foreign.window.fetch, original);
});

test('login observer handles XHR without changing the request body or headers', () => {
  const h = harness();
  const xhr = new h.XHR();
  xhr.open('GET', 'https://api-v2.soundcloud.com/me?client_id=id');
  xhr.setRequestHeader('Authorization', 'OAuth fake-token');
  const body = { unrelated: 'data' };
  xhr.send(body);
  assert.equal(xhr.body, body);
  assert.equal(xhr.headerArgs[1], 'OAuth fake-token');
  assert.equal(h.messages[0].data.access_token, 'fake-token');
});

test('login cookie fallback reads only the dedicated window and installs once', () => {
  const h = harness('https://soundcloud.com', 'other=ignored; oauth_token=fake-token', [{ hydratable: 'apiClient', data: { id: 'id' } }]);
  assert.equal(h.messages[0].data.access_token, 'fake-token');
  const installed = h.window.fetch;
  vm.runInContext(script, h.context);
  assert.equal(h.window.fetch, installed);
  assert.equal(h.messages.length, 1);
});

test('login observer rejects malformed cookie and authorization values', async () => {
  const h = harness('https://soundcloud.com', 'oauth_token=bad%20token', [{ hydratable: 'apiClient', data: { id: 'id' } }]);
  await h.window.fetch('https://api-v2.soundcloud.com/me?client_id=id', { headers: { Authorization: 'Bearer token' } });
  assert.equal(h.messages.length, 0);
});

test('login waits for the protection session and transfers website context without unrelated cookies', async () => {
  const h = harness();
  h.window.ddoptions = { sessionByHeader: true };
  h.window.__sc_version = '1790934937';
  h.context.document.documentElement = { lang: 'ru' };
  await h.window.fetch('https://api-v2.soundcloud.com/me?client_id=id', { headers: { Authorization: 'OAuth fake-token' } });
  assert.equal(h.messages.length, 0);
  h.context.document.cookie = 'unrelated=private; datadome=browser-session~_=';
  h.timers[0]();
  assert.equal(h.messages.length, 1);
  assert.equal(h.messages[0].data.data_dome_client_id, 'browser-session~_=');
  assert.equal(h.messages[0].data.app_version, '1790934937');
  assert.equal(h.messages[0].data.app_locale, 'ru');
  assert.equal(JSON.stringify(h.messages).includes('private'), false);
});

test('login captures the protection header from XHR', () => {
  const h = harness();
  h.window.ddoptions = { sessionByHeader: true };
  const xhr = new h.XHR();
  xhr.open('PUT', 'https://api-v2.soundcloud.com/users/7/track_likes/42?client_id=id&app_version=123&app_locale=en');
  xhr.setRequestHeader('Authorization', 'OAuth fake-token');
  xhr.setRequestHeader('x-datadome-clientid', 'observed-session');
  xhr.send();
  assert.equal(h.messages[0].data.data_dome_client_id, 'observed-session');
  assert.equal(h.messages[0].data.app_version, '123');
});

test('login uses the same dedicated storage value as the website protection tag', async () => {
  const h = harness('https://soundcloud.com', 'datadome=older-cookie');
  h.window.ddoptions = { sessionByHeader: true };
  h.window.localStorage = { getItem: name => name === 'ddSession_datadome' ? 'stored-session' : null };
  await h.window.fetch('https://api-v2.soundcloud.com/me?client_id=id', { headers: { Authorization: 'OAuth fake-token' } });
  assert.equal(h.messages[0].data.data_dome_client_id, 'stored-session');
});

