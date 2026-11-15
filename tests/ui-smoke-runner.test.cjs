const test = require('node:test');
const assert = require('node:assert/strict');
const { spawn, spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { setTimeout: delay } = require('node:timers/promises');

const runner = path.resolve(__dirname, '../scripts/run-ui-smoke.cjs');
const linux = { skip: process.platform !== 'linux', timeout: 25000 };

function fixture(t, mode) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'pookie runner test-'));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const commands = path.join(root, 'commands'); fs.mkdirSync(commands);
    const executable = path.join(root, 'fixture.cjs');
    const capture = path.join(root, 'capture.json');
    fs.writeFileSync(path.join(commands, 'mutter'), `#!/usr/bin/env node
const fs = require('node:fs'); const path = require('node:path');
fs.writeFileSync(path.join(process.env.XDG_RUNTIME_DIR, '.mutter-Xwaylandauth.fixture'), '');
fs.writeFileSync(path.join(process.env.XDG_RUNTIME_DIR, 'pookie-ui-ci'), '');
console.log('Using public X11 display :999');
setInterval(() => {}, 1000);
`, { mode: 0o755 });
    fs.writeFileSync(executable, `#!/usr/bin/env node
const fs = require('node:fs'); const path = require('node:path');
const assert = require('node:assert/strict'); const { spawn } = require('node:child_process');
const root = path.dirname(process.env.XDG_RUNTIME_DIR);
assert.equal(process.env.DISPLAY, ':999');
assert.ok(process.env.XAUTHORITY.startsWith(root + path.sep));
for (const key of ['XDG_RUNTIME_DIR', 'XDG_DATA_HOME', 'XDG_CONFIG_HOME', 'XDG_CACHE_HOME']) {
    assert.equal(path.dirname(process.env[key]), root);
    assert.equal(fs.statSync(process.env[key]).mode & 0o777, 0o700);
}
assert.equal(process.env.POOKIE_FILTERED_QUEUE_ONLY, undefined);
assert.equal(process.env.POOKIE_UI_POOL_ONLY, undefined);
let child;
if (['timeout', 'interrupt'].includes(process.env.RUNNER_TEST_MODE)) {
    process.on('SIGTERM', () => {});
    child = spawn(process.execPath, ['-e', "process.on('SIGTERM', () => {}); setInterval(() => {}, 1000)"], { stdio: 'ignore' });
    setInterval(() => {}, 1000);
}
fs.writeFileSync(process.env.RUNNER_TEST_CAPTURE, JSON.stringify({ root, child: child?.pid }));
if (process.env.RUNNER_TEST_MODE === 'success')
    console.log(process.argv.includes('--feed-ui-smoke-test') ? 'FEED_UI_OK:' : 'SEARCH_QUEUE_UI_OK:');
`, { mode: 0o755 });
    return {
        args: [runner, executable, '--private-bus', '--logs', path.join(root, 'logs')], root, capture,
        env: { ...process.env, PATH: [commands, path.dirname(process.execPath), process.env.PATH].join(path.delimiter),
            RUNNER_TEST_MODE: mode, RUNNER_TEST_CAPTURE: capture, POOKIE_FILTERED_QUEUE_ONLY: '1', POOKIE_UI_POOL_ONLY: '1' },
    };
}

function assertRetired(f) {
    const state = JSON.parse(fs.readFileSync(f.capture, 'utf8'));
    assert.equal(fs.existsSync(state.root), false, 'Private XDG directories survived fixture cleanup');
    if (state.child) {
        const stat = `/proc/${state.child}/stat`;
        assert.ok(!fs.existsSync(stat) || fs.readFileSync(stat, 'utf8').split(' ')[2] === 'Z', 'A fixture worker is still running');
    }
}

test('headless runner isolates storage, resets case flags and requires success markers', linux, t => {
    const f = fixture(t, 'success');
    const result = spawnSync(process.execPath, [...f.args, '--case', 'feed', '--case', 'search-queue'],
        { env: f.env, encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.match(result.stdout, /feed: exit=0 confirmed=true/);
    assert.match(result.stdout, /search-queue: exit=0 confirmed=true/);
    assertRetired(f);
});

test('headless runner rejects a zero exit without its scenario marker', linux, t => {
    const f = fixture(t, 'missing-marker');
    const result = spawnSync(process.execPath, [...f.args, '--case', 'feed'], { env: f.env, encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 1, result.stdout + result.stderr);
    assert.match(result.stdout, /exit=0 confirmed=false/);
    assert.ok(fs.existsSync(path.join(f.root, 'logs/feed.log')));
    assertRetired(f);
});

test('headless timeout retires workers that ignore SIGTERM and preserves its log', linux, t => {
    const f = fixture(t, 'timeout');
    const result = spawnSync(process.execPath, [...f.args, '--case', 'feed', '--timeout', '1'],
        { env: f.env, encoding: 'utf8', timeout: 20000 });
    assert.equal(result.status, 1, result.stdout + result.stderr);
    assert.match(result.stderr, /did not complete within 1s/);
    assert.ok(fs.existsSync(path.join(f.root, 'logs/feed.log')));
    assertRetired(f);
});

test('headless cancellation cleans resources without starting the next case', linux, async t => {
    const f = fixture(t, 'interrupt');
    const child = spawn(process.execPath, [...f.args, '--case', 'feed', '--case', 'search-queue'], { env: f.env, stdio: 'ignore' });
    t.after(() => { if (child.exitCode === null) child.kill('SIGKILL'); });
    const exited = new Promise(resolve => child.once('exit', code => resolve(code)));
    const deadline = performance.now() + 5000;
    while (!fs.existsSync(f.capture) && performance.now() < deadline) await delay(25);
    assert.ok(fs.existsSync(f.capture), 'Fixture did not start');
    child.kill('SIGTERM');
    assert.equal(await exited, 143);
    assert.equal(fs.existsSync(path.join(f.root, 'logs/search-queue.log')), false);
    assertRetired(f);
});
