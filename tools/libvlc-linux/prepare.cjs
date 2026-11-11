#!/usr/bin/env node
// Install a checked archive produced by the current CI build.
const { spawnSync } = require('node:child_process');
const { createHash, randomUUID } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { Transform } = require('node:stream');
const { pipeline } = require('node:stream/promises');
const { runtime, readChecksum, validateBundle } = require('./runtime.cjs');

function run(command, args) {
    const result = spawnSync(command, args, { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
    if (result.error || result.status !== 0) {
        throw new Error(result.error?.message || `${command} failed: ${result.stderr}`);
    }
    return result.stdout;
}

function cached(destination, expected) {
    try {
        const marker = JSON.parse(fs.readFileSync(path.join(destination, '.pookie-runtime.json'), 'utf8'));
        if (marker.version !== runtime.version || marker.rid !== runtime.rid ||
            !/^[a-f0-9]{64}$/.test(marker.sha256) || (expected && marker.sha256 !== expected)) return false;
        validateBundle(destination);
        return true;
    } catch { return false; }
}

// Reject links pointing outside the runtime, including dangling links. The
// verified archive is extracted into an empty staging directory before use.
function validatePaths(directory, root = fs.realpathSync(directory), files = []) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const file = path.join(directory, entry.name);
        const resolved = fs.realpathSync(file);
        if (resolved !== root && !resolved.startsWith(root + path.sep)) {
            throw new Error(`Archive link escapes the Linux libVLC directory: ${file}`);
        }
        if (entry.isDirectory()) validatePaths(file, root, files);
        else if (!entry.isFile() && !entry.isSymbolicLink()) throw new Error(`Unsupported archive entry: ${file}`);
        else if (entry.isFile()) files.push(file);
    }
    return files;
}

async function prepare(destination) {
    const localArchive = process.env.POOKIE_LIBVLC_ARCHIVE || [
        path.resolve(__dirname, '../../artifacts/libvlc-runtime', runtime.asset),
        path.resolve(__dirname, '../../artifacts/libvlc-linux', runtime.asset),
    ].find(file => fs.existsSync(file));
    let expected = process.env.POOKIE_LIBVLC_SHA256;
    if (localArchive && !expected && fs.existsSync(`${localArchive}.sha256`)) {
        expected = readChecksum(fs.readFileSync(`${localArchive}.sha256`, 'utf8'));
    }
    if ((localArchive && !expected) || (expected && !/^[a-f0-9]{64}$/.test(expected))) {
        throw new Error('POOKIE_LIBVLC_ARCHIVE requires POOKIE_LIBVLC_SHA256 (64 lowercase hex characters) or an adjacent .sha256 file.');
    }
    if (cached(destination, expected)) {
        console.log(`Using cached Linux libVLC v${runtime.version}: ${destination}`);
        return;
    }
    if (!localArchive) {
        throw new Error('No packaged Linux libVLC runtime is available. Download the libvlc-linux-x64 Actions artifact ' +
            'into artifacts/libvlc-runtime, or set POOKIE_LIBVLC_ARCHIVE and its SHA-256. ' +
            'CI prepares the archive during the application build.');
    }
    const work = fs.mkdtempSync(path.join(path.dirname(destination), 'libvlc-extract-'));
    try {
        const archive = path.join(work, runtime.asset);
        const hash = createHash('sha256');
        console.log(`Preparing Linux libVLC v${runtime.version} (${runtime.baseline})...`);
        const input = fs.createReadStream(path.resolve(localArchive));
        await pipeline(input, new Transform({
            transform(chunk, encoding, callback) { hash.update(chunk); callback(null, chunk); },
        }), fs.createWriteStream(archive));
        const actual = hash.digest('hex');
        if (actual !== expected) throw new Error(`Linux libVLC archive SHA-256 mismatch: expected ${expected}, got ${actual}.`);

        const staging = path.join(work, 'runtime');
        fs.mkdirSync(staging);
        const entries = run('tar', ['-tzf', archive]).split('\n').filter(Boolean);
        if (!entries.length || entries.some(entry => path.posix.isAbsolute(entry) || entry.split('/').includes('..'))) {
            throw new Error('Unsafe paths in Linux libVLC archive.');
        }
        run('tar', ['-xzf', archive, '--directory', staging, '--no-same-owner']);
        const files = validatePaths(staging);
        validateBundle(staging);
        // Archive metadata is deterministic (mtime=0), but MSBuild's
        // PreserveNewest must see newly installed runtime files as newer.
        const installedAt = new Date();
        for (const file of files) fs.utimesSync(file, installedAt, installedAt);
        fs.writeFileSync(path.join(staging, '.pookie-runtime.json'), JSON.stringify({
            version: runtime.version, rid: runtime.rid, sha256: actual,
        }, null, 2) + '\n');
        // Only replace the previous cache after verification and extraction.
        // All builders use the same kernel lock, including when updating versions.
        const previous = `${destination}.previous-${randomUUID()}`;
        const hadPrevious = fs.existsSync(destination);
        if (hadPrevious) fs.renameSync(destination, previous);
        try { fs.renameSync(staging, destination); }
        catch (error) {
            if (hadPrevious) fs.renameSync(previous, destination);
            throw error;
        }
        if (hadPrevious) fs.rmSync(previous, { recursive: true, force: true });
        console.log(`Verified Linux libVLC v${runtime.version}: ${destination}`);
    } finally {
        fs.rmSync(work, { recursive: true, force: true });
    }
}

async function main() {
    const args = process.argv.slice(2);
    if (args[0] !== '--output' || !args[1] || args.length > 3 || (args[2] && args[2] !== '--locked')) {
        throw new Error('Usage: node prepare.cjs --output <directory>');
    }
    const destination = path.resolve(args[1]);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    if (args[2] === '--locked') {
        await prepare(destination);
    } else {
        const result = spawnSync('flock', ['--exclusive', `${destination}.lock`,
            process.execPath, __filename, '--output', destination, '--locked'], { stdio: 'inherit' });
        if (result.error) throw new Error(`Linux libVLC preparation requires flock: ${result.error.message}`);
        process.exitCode = result.status ?? 1;
    }
}

main().catch(error => { console.error(`Linux libVLC: ${error.message}`); process.exitCode = 1; });
