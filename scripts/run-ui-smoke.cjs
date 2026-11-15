#!/usr/bin/env node
// SPDX-FileCopyrightText: 2026 Daniel Freak
// SPDX-License-Identifier: BSD-3-Clause
// Run account-free UI fixtures in their own D-Bus/compositor/data directories.
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const { tmpdir } = require('node:os');
const path = require('node:path');
const { parseArgs } = require('node:util');
const { setTimeout: delay } = require('node:timers/promises');

const cases = {
    feed: ['--feed-ui-smoke-test', {}, 'FEED_UI_OK:'],
    'search-queue': ['--scrolling-ui-smoke-test', { POOKIE_SEARCH_QUEUE_ONLY: '1' }, 'SEARCH_QUEUE_UI_OK:'],
    'filtered-queue': ['--scrolling-ui-smoke-test', { POOKIE_FILTERED_QUEUE_ONLY: '1' }, 'FILTERED_QUEUE_UI_OK:'],
    'global-playback': ['--scrolling-ui-smoke-test', { POOKIE_GLOBAL_PLAYBACK_ONLY: '1' }, 'GLOBAL_PLAYBACK_UI_OK:'],
    'pooled-pages': ['--scrolling-ui-smoke-test', { POOKIE_UI_POOL_ONLY: '1' }, 'UI_PAGE_POOLS_OK:'],
    'track-page': ['--track-page-ui-smoke-test', {}, 'TRACK_PAGE_UI_OK:'],
    settings: ['--settings-ui-smoke-test', {}, 'UI_SETTINGS_OK:'],
    memory: ['--memory-ui-smoke-test', {}, 'ARTWORK_LIFETIME_OK:'],
    chats: ['--chats-ui-smoke-test', {}, 'UI_CHATS_OK:'],
};
const caseFlags = [...new Set(Object.values(cases).flatMap(([, overrides]) => Object.keys(overrides)))];
const active = new Set();
const temporaryRoots = new Set();
let interruption = 0;

function launch(command, args, options) {
    const child = spawn(command, args, { detached: true, ...options });
    const job = { child, finished: false, result: null };
    job.exited = new Promise(resolve => {
        function finish(result) {
            if (job.finished) return;
            job.finished = true;
            job.result = result;
            resolve(result);
        }
        child.once('error', error => finish({ code: 1, error }));
        child.once('close', (code, signal) => finish({ code: code ?? 1, signal }));
    });
    active.add(job);
    return job;
}

async function wait(job, milliseconds) {
    let timer;
    try {
        return await Promise.race([
            job.exited,
            new Promise(resolve => { timer = setTimeout(() => resolve(null), milliseconds); }),
        ]);
    } finally { clearTimeout(timer); }
}

function signalGroup(job, signal) {
    if (!job.child.pid) return false;
    try { process.kill(-job.child.pid, signal); return true; }
    catch (error) { if (error.code !== 'ESRCH') throw error; }
    return false;
}

async function stop(job) {
    if (!job) return;
    // A worker can outlive its parent. Retire the whole owned process group,
    // including when the fixture has already exited or its timeout has fired.
    signalGroup(job, 'SIGTERM');
    const deadline = performance.now() + 5000;
    while (signalGroup(job, 0) && performance.now() < deadline) await delay(50);
    signalGroup(job, 'SIGKILL');
    await wait(job, 5000);
    active.delete(job);
}

function launchLogged(command, args, env, log) {
    const output = fs.openSync(log, 'w', 0o600);
    try { return launch(command, args, { env, stdio: ['ignore', output, output] }); }
    finally { fs.closeSync(output); }
}

async function main() {
    const { values, positionals } = parseArgs({
        allowPositionals: true,
        options: {
            case: { type: 'string', multiple: true },
            logs: { type: 'string', default: 'artifacts/ui-smoke' },
            timeout: { type: 'string', default: '120' },
            'private-bus': { type: 'boolean' },
            help: { type: 'boolean', short: 'h' },
        },
    });
    if (values.help) {
        console.log('Usage: node scripts/run-ui-smoke.cjs EXECUTABLE [--case NAME] [--logs PATH] [--timeout SECONDS]');
        console.log(`Cases (repeat --case to select several): ${Object.keys(cases).join(', ')}`);
        return 0;
    }
    if (positionals.length !== 1) throw new Error('Provide one fixture executable; use --help for usage.');
    if (!/^\d+$/.test(values.timeout) || Number(values.timeout) <= 0 || Number(values.timeout) > 86400)
        throw new Error('--timeout must be an integer between 1 and 86400 seconds.');
    for (const name of values.case ?? [])
        if (!Object.hasOwn(cases, name)) throw new Error(`Unknown fixture case: ${name}`);
    if (process.platform !== 'linux') throw new Error('The private Mutter/Xwayland runner requires Linux.');
    const executable = fs.realpathSync(positionals[0]);
    fs.accessSync(executable, fs.constants.X_OK);
    if (!values['private-bus']) {
        const bus = launch('dbus-run-session', ['--', process.execPath, __filename, ...process.argv.slice(2), '--private-bus'],
            { stdio: 'inherit' });
        const result = await bus.exited;
        active.delete(bus);
        if (result.error) throw result.error;
        return result.code;
    }
    const logs = path.resolve(values.logs);
    fs.mkdirSync(logs, { recursive: true });
    const root = fs.mkdtempSync(path.join(tmpdir(), 'pookie-ui-ci-'));
    temporaryRoots.add(root);
    const env = { ...process.env };
    for (const key of ['DISPLAY', 'XAUTHORITY', 'SESSION_MANAGER', 'AT_SPI_BUS_ADDRESS']) delete env[key];
    for (const [key, name] of Object.entries({ XDG_RUNTIME_DIR: 'run', XDG_DATA_HOME: 'data', XDG_CONFIG_HOME: 'config', XDG_CACHE_HOME: 'cache' })) {
        const directory = path.join(root, name);
        fs.mkdirSync(directory, { mode: 0o700 });
        env[key] = directory;
    }
    Object.assign(env, {
        GDK_BACKEND: 'x11', GIO_USE_VFS: 'local', GTK_IM_MODULE: 'simple', NO_AT_BRIDGE: '1',
        XDG_CURRENT_DESKTOP: 'GNOME', XDG_SESSION_TYPE: 'wayland', WAYLAND_DISPLAY: 'pookie-ui-ci',
        POOKIE_LIBRARY_SCROLL_PERF: '0', POOKIE_VIDEO_SILENT: '1', LIBGL_ALWAYS_SOFTWARE: '1',
    });
    let compositor;
    try {
        const log = path.join(logs, 'compositor.log');
        compositor = launchLogged('mutter', ['--headless', '--wayland', '--virtual-monitor=1600x1000@60',
            '--wayland-display=pookie-ui-ci'], env, log);
        const deadline = performance.now() + 30000;
        while (true) {
            if (interruption) return interruption;
            if (compositor.finished) throw compositor.result.error ?? new Error('Private compositor exited; see compositor.log');
            const display = fs.readFileSync(log, 'utf8').match(/Using public X11 display (:\d+)/);
            const authentication = fs.readdirSync(env.XDG_RUNTIME_DIR).find(name => name.includes('Xwaylandauth'));
            if (display && authentication && fs.existsSync(path.join(env.XDG_RUNTIME_DIR, env.WAYLAND_DISPLAY))) {
                Object.assign(env, { DISPLAY: display[1], XAUTHORITY: path.join(env.XDG_RUNTIME_DIR, authentication) });
                break;
            }
            if (performance.now() >= deadline) throw new Error('Private compositor did not start within 30 seconds.');
            await delay(100);
        }
        for (const name of values.case ?? Object.keys(cases)) {
            if (interruption) return interruption;
            const [command, overrides, marker] = cases[name];
            const runEnv = { ...env };
            for (const flag of caseFlags) delete runEnv[flag];
            Object.assign(runEnv, overrides);
            const log = path.join(logs, `${name}.log`);
            const fixture = launchLogged(executable, [command, '--no-audio'], runEnv, log);
            let result;
            try { result = await wait(fixture, Number(values.timeout) * 1000); }
            finally { await stop(fixture); }
            if (interruption) return interruption;
            if (!result) throw new Error(`${name} did not complete within ${values.timeout}s; see ${log}`);
            if (result.error) throw result.error;
            const output = fs.readFileSync(log, 'utf8');
            console.log(`UI_FIXTURE ${name}: exit=${result.code} confirmed=${output.includes(marker)}`);
            if (result.code !== 0 || !output.includes(marker)) {
                console.error(output);
                return result.code || 1;
            }
        }
    } finally {
        await stop(compositor);
        fs.rmSync(root, { recursive: true, force: true });
        temporaryRoots.delete(root);
    }
    return 0;
}

// CI cancellation must not leave a compositor or fixture worker running.
for (const signal of ['SIGINT', 'SIGTERM']) {
    process.once(signal, () => {
        interruption = signal === 'SIGINT' ? 130 : 143;
        void Promise.allSettled([...active].map(stop)).then(() => {
            for (const root of temporaryRoots) fs.rmSync(root, { recursive: true, force: true });
            process.exit(interruption);
        });
    });
}
main().then(code => { process.exitCode = interruption || code; }, error => {
    console.error(error.message);
    process.exitCode = interruption || 1;
});
