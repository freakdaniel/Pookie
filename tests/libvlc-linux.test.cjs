const assert = require('node:assert/strict');
const { spawn, spawnSync } = require('node:child_process');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const { tmpdir } = require('node:os');
const path = require('node:path');
const test = require('node:test');
const { runtime } = require('../tools/libvlc-linux/runtime.cjs');

const script = path.resolve(__dirname, '../tools/libvlc-linux/prepare.cjs');
const linux = { skip: process.platform !== 'linux' };
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

function fixture(t) {
    const root = fs.mkdtempSync(path.join(tmpdir(), 'pookie vlc test-'));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const input = path.join(root, 'input');
    const output = path.join(root, 'runtime with spaces');
    const commands = path.join(root, 'commands');
    fs.mkdirSync(input);
    fs.mkdirSync(output);
    fs.mkdirSync(commands);
    // Exercise the real archive installer with no package manager available.
    for (const command of ['flock', 'tar', 'gzip']) {
        const binary = process.env.PATH.split(path.delimiter).map(directory => path.join(directory, command))
            .find(file => fs.existsSync(file));
        assert.ok(binary, `Linux tests require ${command}`);
        fs.symlinkSync(binary, path.join(commands, command));
    }
    const manifest = { version: runtime.version, rid: runtime.rid, packages: [] };
    fs.writeFileSync(path.join(input, 'manifest.json'), JSON.stringify(manifest));
    for (const file of ['libvlc.so.5', 'libvlccore.so.9', 'vlc/plugins/codec/libavcodec_plugin.so',
        'vlc/plugins/demux/libavformat_plugin.so', 'licenses/README.txt']) {
        fs.mkdirSync(path.dirname(path.join(input, file)), { recursive: true });
        fs.writeFileSync(path.join(input, file), 'fixture runtime');
    }
    fs.symlinkSync('libvlc.so.5', path.join(input, 'libvlc.so'));
    fs.symlinkSync('libvlccore.so.9', path.join(input, 'libvlccore.so'));
    fs.writeFileSync(path.join(output, 'keep.txt'), 'previous runtime');
    const archive = path.join(root, runtime.asset);
    const repack = () => {
        const result = spawnSync('tar', ['-czf', archive, '-C', input, '.'], { encoding: 'utf8' });
        assert.equal(result.status, 0, result.stderr);
        return sha256(fs.readFileSync(archive));
    };
    const hash = repack();
    const env = { ...process.env, PATH: commands, POOKIE_LIBVLC_ARCHIVE: archive, POOKIE_LIBVLC_SHA256: hash };
    const invoke = (extraEnv = {}, entry = script) => spawnSync(process.execPath, [entry, '--output', output], {
        encoding: 'utf8', env: { ...env, ...extraEnv }, timeout: 15_000,
    });
    const noTemporaryFiles = () => assert.deepEqual(fs.readdirSync(root)
        .filter(name => name.startsWith('libvlc-extract-') || name.includes('.previous-')), []);
    return { root, input, output, commands, archive, hash, env, repack, invoke, noTemporaryFiles };
}

function isolatedTooling(f) {
    const directory = path.join(f.root, 'tools/libvlc-linux');
    fs.mkdirSync(directory, { recursive: true });
    for (const file of ['prepare.cjs', 'runtime.cjs', 'runtime.json']) {
        fs.copyFileSync(path.join(path.dirname(script), file), path.join(directory, file));
    }
    return path.join(directory, 'prepare.cjs');
}

test('Linux libVLC installs a checked archive without apt, dpkg or Python', linux, t => {
    const f = fixture(t);
    const result = f.invoke();
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /Verified Linux libVLC v/);
    assert.equal(fs.readlinkSync(path.join(f.output, 'libvlc.so')), 'libvlc.so.5');
    assert.equal(JSON.parse(fs.readFileSync(path.join(f.output, '.pookie-runtime.json'))).sha256, f.hash);
    assert.equal(fs.existsSync(path.join(f.output, 'keep.txt')), false);
    f.noTemporaryFiles();
});

test('Linux libVLC consumes the workflow artifact and reuses it offline', linux, t => {
    const f = fixture(t);
    const entry = isolatedTooling(f);
    const artifact = path.join(f.root, 'artifacts/libvlc-runtime', runtime.asset);
    fs.mkdirSync(path.dirname(artifact), { recursive: true });
    fs.copyFileSync(f.archive, artifact);
    fs.writeFileSync(`${artifact}.sha256`, `${f.hash}  ${runtime.asset}\n`);
    const env = { POOKIE_LIBVLC_ARCHIVE: undefined, POOKIE_LIBVLC_SHA256: undefined };
    const result = f.invoke(env, entry);
    assert.equal(result.status, 0, result.stderr);
    fs.rmSync(path.join(f.root, 'artifacts'), { recursive: true });
    const cached = f.invoke(env, entry);
    assert.equal(cached.status, 0, cached.stderr);
    assert.match(cached.stdout, /Using cached Linux libVLC/);
    f.noTemporaryFiles();
});

test('Linux libVLC rejects an altered archive before running tar and keeps the old cache', linux, t => {
    const f = fixture(t);
    fs.appendFileSync(f.archive, 'tampered');
    fs.unlinkSync(path.join(f.commands, 'tar'));
    const result = f.invoke();
    assert.equal(result.status, 1);
    assert.match(result.stderr, /archive SHA-256 mismatch/);
    assert.equal(fs.readFileSync(path.join(f.output, 'keep.txt'), 'utf8'), 'previous runtime');
    f.noTemporaryFiles();
});

test('Linux libVLC rejects a malformed adjacent checksum', linux, t => {
    const f = fixture(t);
    fs.writeFileSync(`${f.archive}.sha256`, `${f.hash}  another-asset.tar.gz\n`);
    const result = f.invoke({ POOKIE_LIBVLC_SHA256: undefined });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /Invalid Linux libVLC checksum file/);
    f.noTemporaryFiles();
});

test('Linux libVLC reports a missing CI artifact without requiring a published runtime', linux, t => {
    const f = fixture(t);
    const result = f.invoke({ POOKIE_LIBVLC_ARCHIVE: undefined, POOKIE_LIBVLC_SHA256: undefined }, isolatedTooling(f));
    assert.equal(result.status, 1);
    assert.match(result.stderr, /Download the libvlc-linux-x64 Actions artifact/);
    assert.doesNotMatch(result.stderr, /releases\/download|published/);
    assert.equal(fs.readFileSync(path.join(f.output, 'keep.txt'), 'utf8'), 'previous runtime');
    f.noTemporaryFiles();
});

test('Linux libVLC requires an explicit hash for a CI/local archive', linux, t => {
    const f = fixture(t);
    const result = f.invoke({ POOKIE_LIBVLC_SHA256: undefined });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /requires POOKIE_LIBVLC_SHA256/);
});

test('Linux libVLC preserves the cache if extraction fails', linux, t => {
    const f = fixture(t);
    fs.writeFileSync(f.archive, 'not a gzip archive');
    const result = f.invoke({ POOKIE_LIBVLC_SHA256: sha256(fs.readFileSync(f.archive)) });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /tar failed/);
    assert.equal(fs.readFileSync(path.join(f.output, 'keep.txt'), 'utf8'), 'previous runtime');
    f.noTemporaryFiles();
});

test('Linux libVLC rejects archives for a different version or architecture', linux, t => {
    const f = fixture(t);
    fs.writeFileSync(path.join(f.input, 'manifest.json'), JSON.stringify({ version: runtime.version, rid: 'linux-arm64' }));
    const result = f.invoke({ POOKIE_LIBVLC_SHA256: f.repack() });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /version\/architecture does not match/);
    assert.equal(fs.readFileSync(path.join(f.output, 'keep.txt'), 'utf8'), 'previous runtime');
    f.noTemporaryFiles();
});

test('Linux libVLC rejects links outside the extracted runtime', linux, t => {
    const f = fixture(t);
    const outside = path.join(f.root, 'outside.txt');
    fs.writeFileSync(outside, 'outside');
    fs.symlinkSync(outside, path.join(f.input, 'escape.so'));
    const result = f.invoke({ POOKIE_LIBVLC_SHA256: f.repack() });
    assert.equal(result.status, 1);
    assert.match(result.stderr, /Archive link escapes/);
    assert.equal(fs.readFileSync(outside, 'utf8'), 'outside');
    f.noTemporaryFiles();
});

test('Linux libVLC replaces a cached runtime when the expected hash changes', linux, t => {
    const f = fixture(t);
    assert.equal(f.invoke().status, 0);
    fs.writeFileSync(path.join(f.input, 'libvlc.so.5'), 'updated fixture runtime');
    const updated = f.repack();
    const result = f.invoke({ POOKIE_LIBVLC_SHA256: updated });
    assert.equal(result.status, 0, result.stderr);
    assert.equal(fs.readFileSync(path.join(f.output, 'libvlc.so.5'), 'utf8'), 'updated fixture runtime');
    assert.equal(JSON.parse(fs.readFileSync(path.join(f.output, '.pookie-runtime.json'))).sha256, updated);
    f.noTemporaryFiles();
});

test('concurrent Linux libVLC builds share a verified cache', linux, async t => {
    const f = fixture(t);
    const invoke = () => new Promise((resolve, reject) => {
        const child = spawn(process.execPath, [script, '--output', f.output], { env: f.env });
        let stdout = '', stderr = '';
        child.stdout.on('data', chunk => { stdout += chunk; });
        child.stderr.on('data', chunk => { stderr += chunk; });
        child.on('error', reject);
        child.on('close', status => resolve({ status, stdout, stderr }));
    });
    const results = await Promise.all([invoke(), invoke()]);
    for (const result of results) assert.equal(result.status, 0, result.stderr);
    assert.equal(results.filter(result => result.stdout.includes('Verified Linux libVLC')).length, 1);
    assert.equal(results.filter(result => result.stdout.includes('Using cached Linux libVLC')).length, 1);
    f.noTemporaryFiles();
});

test('runtime packaging is deterministic and installation refreshes timestamps for MSBuild', linux, t => {
    const f = fixture(t);
    const pack = path.resolve(__dirname, '../tools/libvlc-linux/pack-runtime.cjs');
    const first = path.join(f.root, 'first archive');
    const second = path.join(f.root, 'second archive');
    const invokePack = output => {
        const result = spawnSync(process.execPath, [pack, '--input', f.input, '--output', output], { encoding: 'utf8' });
        assert.equal(result.status, 0, result.stderr);
        return fs.readFileSync(path.join(output, runtime.asset));
    };
    const bytes = invokePack(first);
    fs.utimesSync(path.join(f.input, 'libvlc.so.5'), new Date(123456), new Date(123456));
    assert.deepEqual(invokePack(second), bytes, 'Runtime archive must not depend on source timestamps');
    const expected = sha256(bytes);
    assert.equal(fs.readFileSync(path.join(first, `${runtime.asset}.sha256`), 'utf8'), `${expected}  ${runtime.asset}\n`);
    const installedAt = Date.now();
    const result = f.invoke({ POOKIE_LIBVLC_ARCHIVE: path.join(first, runtime.asset), POOKIE_LIBVLC_SHA256: expected });
    assert.equal(result.status, 0, result.stderr);
    assert.ok(fs.statSync(path.join(f.output, 'libvlc.so.5')).mtimeMs >= installedAt - 1,
        'A deterministic archive must not prevent MSBuild PreserveNewest from updating native libraries');
});

test('Linux libVLC repairs an incomplete cached runtime', linux, t => {
    const f = fixture(t);
    assert.equal(f.invoke().status, 0);
    const plugin = path.join(f.output, 'vlc/plugins/codec/libavcodec_plugin.so');
    fs.unlinkSync(plugin);
    const result = f.invoke();
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /Verified Linux libVLC/);
    assert.equal(fs.readFileSync(plugin, 'utf8'), 'fixture runtime');
});
