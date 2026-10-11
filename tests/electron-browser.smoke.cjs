// Real Electron/Chromium integration test; requires a built browser host and a desktop display.
// Run separately: node tests/electron-browser.smoke.cjs (no SoundCloud traffic or real profile).
const assert = require('node:assert/strict');
const { createServer } = require('node:http');
const { spawn } = require('node:child_process');
const { mkdtempSync, rmSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { join, resolve } = require('node:path');

const helper = process.env.POOKIE_ELECTRON_TEST_HOST || resolve(__dirname, '../src/Pookie.Browser.Electron/bin/Debug/net10.0/Pookie.Browser.Electron.dll');
const profile = mkdtempSync(join(tmpdir(), 'pookie-electron-smoke-'));
let signIns = 0, reads = 0;
const fakeToken = 'fixture-secret-must-never-be-logged';
const requests = [];
const frameChecks = [];
const popupChecks = [];
const server = createServer((request, response) => {
  const url = new URL(request.url, 'http://fixture');
  if (url.pathname === '/popup-check') {
    popupChecks.push(url.searchParams.get('result'));
    response.end('{}');
    return;
  }
  if (url.pathname === '/popup') {
    response.setHeader('Content-Type', 'text/html');
    response.end(`<script>
      opener.postMessage({ kind:'popup', isolated:typeof require === 'undefined' && typeof process === 'undefined' }, location.origin);
      close();
    </script>`);
    return;
  }
  if (url.pathname === '/frame-check') {
    frameChecks.push(url.searchParams.get('bridge'));
    response.end('{}');
    return;
  }
  if (url.pathname === '/api/sign-in') {
    signIns++;
    response.setHeader('Set-Cookie', `oauth_token=${fakeToken}; HttpOnly; Path=/; Max-Age=3600; SameSite=Lax`);
    response.end('{}');
    return;
  }
  if (url.pathname === '/api/me') {
    reads++;
    const authorized = request.headers.authorization === `OAuth ${fakeToken}` &&
      request.headers.cookie?.includes(`oauth_token=${fakeToken}`) && url.searchParams.get('client_id') === 'fixtureid';
    requests.push({ authorized, userAgent: request.headers['user-agent'], restart: signIns === 1 && reads > 1 });
    response.writeHead(authorized ? 200 : 401, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify(authorized ? { id: 42, username: 'Fixture listener' } : {}));
    return;
  }
  if (url.pathname === '/frame') {
    response.setHeader('Content-Type', 'text/html');
    response.end(`<script>parent.postMessage({ kind:'frame', bridge:typeof window.pookiePrototype }, '*')</script>`);
    return;
  }
  response.setHeader('Content-Type', 'text/html');
  response.end(`<!doctype html><html lang="en"><title>Local SoundCloud fixture</title>
    <h1>Browser fixture</h1><iframe src="/frame"></iframe><script>
    window.__sc_hydration = [{ hydratable:'apiClient', data:{ id:'fixtureid' } }];
    window.__sc_version = '1.0';
    addEventListener('message', event => {
      if (event.data.kind === 'frame') fetch('/frame-check?bridge=' + event.data.bridge);
    });
    (async () => {
      const first = new URL(location.href).searchParams.has('signIn');
      if (first) {
        // Same pattern as SoundCloud OAuth: open blank now, navigate later.
        const completed = new Promise(resolvePopup => addEventListener('message', event => {
          if (event.origin === location.origin && event.data.kind === 'popup') resolvePopup(event.data.isolated);
        }));
        const popup = window.open('about:blank', 'fixture-oauth', 'width=400,height=300');
        if (!popup) { await fetch('/popup-check?result=blocked'); return; }
        setTimeout(() => popup.location.href = '/popup', 50);
        const isolated = await completed;
        const unsafe = window.open('file:///etc/passwd', 'unsafe');
        await fetch('/popup-check?result=' + (isolated && unsafe === null ? 'ok' : 'unsafe'));
        await fetch('/api/sign-in'); localStorage.setItem('durable-fixture', 'yes');
      }
      const checks = typeof require === 'undefined' && typeof process === 'undefined' &&
        typeof window.infiniframe === 'undefined' && localStorage.getItem('durable-fixture') === 'yes';
      if (!checks) return;
      // On restart only persisted cookies/storage can authorize this request.
      await fetch('/api/me?client_id=fixtureid', { headers: first ? { Authorization:'OAuth ${fakeToken}' } : {} });
    })();
    </script></html>`);
});

function run(args, timeout = 60000) {
  return new Promise((resolveResult, reject) => {
    const child = spawn(helper.endsWith('.dll') ? 'dotnet' : helper, [...(helper.endsWith('.dll') ? [helper] : []), '-unpackeddotnet', ...args], { stdio: ['ignore', 'pipe', 'pipe'] });
    let stdout = '', stderr = '';
    child.stdout.on('data', data => stdout += data);
    child.stderr.on('data', data => stderr += data);
    const timer = setTimeout(() => { child.kill('SIGINT'); reject(new Error('Browser helper did not finish')); }, timeout);
    child.on('error', error => { clearTimeout(timer); reject(error); });
    child.on('exit', code => {
      clearTimeout(timer);
      assert(!stdout.includes(fakeToken) && !stderr.includes(fakeToken), 'Credentials leaked to output');
      const events = stdout.split('\n').filter(line => line.startsWith('POOKIE_ELECTRON_RESULT '))
        .map(line => JSON.parse(line.slice('POOKIE_ELECTRON_RESULT '.length)));
      resolveResult({ code, events, stderr });
    });
  });
}

(async () => {
  await new Promise(resolveReady => server.listen(0, '127.0.0.1', resolveReady));
  const origin = `http://127.0.0.1:${server.address().port}`;
  const login = await run(['--verify-session', '--fixture', origin + '/?signIn', '--profile', profile, '--timeout', '30']);
  assert.equal(login.code, 0, JSON.stringify(login.events) + login.stderr);
  const first = login.events.find(event => event.kind === 'profile');
  assert.equal(first.user.id, 42);
  assert.match(first.userAgent, /Chrome\//);
  assert.equal(first.runtime.electron, '44.7.0');
  assert.equal(first.runtime.nodeIntegration, false);
  assert.equal(first.runtime.contextIsolation, true);
  assert.equal(first.runtime.sandbox, true);
  assert.deepEqual(popupChecks, ['ok'], 'Blank OAuth popup or isolated popup navigation failed');
  const verify = await run(['--verify-session', '--fixture', origin, '--profile', profile, '--timeout', '30']);
  assert.equal(verify.code, 0, JSON.stringify(verify.events) + verify.stderr);
  assert.equal(verify.events.find(event => event.kind === 'profile').user.id, 42);
  assert.equal(signIns, 1, 'Restart unexpectedly signed in again');
  assert.equal(reads, 4, 'Each run should make one fixture page read and one host /me read');
  assert.equal(requests.at(-1).authorized, true);
  assert.equal(requests.at(-1).restart, true);
  assert.equal(requests.at(-1).userAgent, first.userAgent, 'HTTP and navigator User-Agent differ');
  assert(frameChecks.length >= 2 && frameChecks.every(value => value === 'undefined'), 'Host bridge leaked to iframe');
  // Missing session must fail, never invent or copy credentials from another browser.
  const emptyProfile = mkdtempSync(join(tmpdir(), 'pookie-electron-empty-'));
  try {
    const empty = await run(['--verify-session', '--fixture', origin, '--profile', emptyProfile, '--timeout', '4']);
    assert.equal(empty.code, 1);
    assert(empty.events.some(event => event.kind === 'timeout-or-cancelled'));
  } finally { rmSync(emptyProfile, { recursive: true, force: true }); }
  const invalid = await run(['--fixture', 'http://example.com', '--profile', profile]);
  assert.equal(invalid.code, 2);
  console.log('Electron browser smoke passed: isolated Chromium, durable cookie/localStorage, restart /me, missing session, privacy.');
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => {
  await new Promise(resolveClosed => server.close(resolveClosed));
  rmSync(profile, { recursive: true, force: true });
});
