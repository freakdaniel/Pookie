#!/usr/bin/env node
// Build a relocatable VLC 3 runtime from signed Ubuntu 22.04 packages, without root.
const { spawnSync } = require('node:child_process');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const { userInfo } = require('node:os');
const path = require('node:path');
const { runtime } = require('./runtime.cjs');

// The host supplies glibc and its loader; ship the remaining ELF dependencies.
const HOST_LIBRARIES = new Set([
    'libc.so.6', 'libm.so.6', 'libdl.so.2', 'libpthread.so.0', 'librt.so.1',
    'libresolv.so.2', 'libutil.so.1', 'ld-linux-x86-64.so.2',
]);
const KEYRING_URL = 'https://archive.ubuntu.com/ubuntu/project/ubuntu-archive-keyring.gpg';
// Pin the official archive keyring, including signing keys 2012 and 2018.
// 2018 fingerprint: F6ECB3762474EDA9D21B7022871920D1991BC93C.
const KEYRING_SHA256 = '80a36b0a6de2f69f49d2df75ef473ccde121e9e190b9ea01d20a4f63778d5c31';
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');

function run(command, args, inherit = false, env = process.env) {
    const result = spawnSync(command, args, {
        encoding: 'utf8', maxBuffer: 32 * 1024 * 1024,
        stdio: inherit ? 'inherit' : 'pipe', env,
    });
    if (result.error) throw new Error(`Cannot run ${command}: ${result.error.message}`);
    if (result.status !== 0) {
        throw new Error(`${command} failed (${result.signal || result.status}).\n${result.stdout || ''}${result.stderr || ''}`);
    }
    return result.stdout;
}

function filesIn(directory) {
    if (!fs.existsSync(directory)) return [];
    return fs.readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))
        .flatMap(entry => {
            const file = path.join(directory, entry.name);
            return entry.isDirectory() ? filesIn(file) : [file];
        });
}

async function prepare(destination) {
    const fingerprint = sha256(Buffer.concat([fs.readFileSync(__filename), Buffer.from(JSON.stringify(runtime))]));
    const marker = path.join(destination, 'manifest.json');
    if (fs.existsSync(marker) && JSON.parse(fs.readFileSync(marker, 'utf8')).builder === fingerprint) {
        console.log(`Using cached Linux libVLC: ${destination}`);
        return;
    }
    for (const command of ['apt-get', 'dpkg-deb', 'readelf']) run(command, ['--version']);

    const work = fs.mkdtempSync(path.join(path.dirname(destination), 'libvlc-'));
    try {
        for (const directory of ['lists/partial', 'archives/partial', 'logs', 'root', 'bundle', 'apt.conf.d']) {
            fs.mkdirSync(path.join(work, directory), { recursive: true });
        }
        fs.writeFileSync(path.join(work, 'status'), '');
        // Debian has apt/dpkg but does not ship ubuntu-keyring. Keep trust local
        // to this build and verify the download before giving it to apt.
        console.log('Downloading pinned Ubuntu archive keyring...');
        const response = await fetch(KEYRING_URL, { signal: AbortSignal.timeout(60_000) });
        if (!response.ok) throw new Error(`Ubuntu keyring download failed: HTTP ${response.status}`);
        const keyringBytes = Buffer.from(await response.arrayBuffer());
        if (sha256(keyringBytes) !== KEYRING_SHA256) throw new Error('Ubuntu archive keyring SHA-256 mismatch.');
        const keyring = path.join(work, 'ubuntu-archive-keyring.gpg');
        fs.writeFileSync(keyring, keyringBytes);
        fs.writeFileSync(path.join(work, 'sources.list'), [
            ['archive.ubuntu.com', 'jammy'], ['archive.ubuntu.com', 'jammy-updates'],
            ['security.ubuntu.com', 'jammy-security'],
        ].map(([server, suite]) =>
            `deb [arch=amd64 signed-by="${keyring}"] https://${server}/ubuntu ${suite} main universe\n`).join(''));
        // APT_CONFIG is read before the host's apt.conf.d. Command-line -o
        // overrides alone are too late to disable host hooks and extra indexes.
        const aptConfig = path.join(work, 'apt.conf');
        fs.writeFileSync(aptConfig, `Dir::Etc::main "/dev/null";\nDir::Etc::parts ${JSON.stringify(path.join(work, 'apt.conf.d'))};\n`);
        const aptEnv = { ...process.env, APT_CONFIG: aptConfig };
        const options = {
            'Dir::Etc::sourcelist': path.join(work, 'sources.list'), 'Dir::Etc::sourceparts': '-',
            'Dir::State::lists': path.join(work, 'lists'), 'Dir::State::status': path.join(work, 'status'),
            'Dir::Cache::archives': path.join(work, 'archives'), 'Dir::Log': path.join(work, 'logs'),
            'Dir::Cache::pkgcache': path.join(work, 'pkgcache.bin'),
            'Dir::Cache::srcpkgcache': path.join(work, 'srcpkgcache.bin'),
            'APT::Architecture': 'amd64', 'APT::Architectures::': 'amd64',
            'APT::Sandbox::User': userInfo().username, 'Debug::NoLocking': 'true',
            'Acquire::Languages': 'none', 'Acquire::Retries': '3',
        };
        const apt = Object.entries(options).flatMap(([key, value]) => ['-o', `${key}=${value}`]);
        console.log('Downloading verified Ubuntu 22.04 libVLC and codec dependencies (no system installation)...');
        run('apt-get', [...apt, 'update', '--error-on=any'], true, aptEnv);
        run('apt-get', [...apt, '--yes', '--download-only', '--no-install-recommends', '--no-install-suggests',
            'install', 'libvlc5', 'vlc-plugin-base', 'patchelf'], true, aptEnv);
        const root = path.join(work, 'root');
        const bundle = path.join(work, 'bundle');
        const packages = [];
        for (const archive of filesIn(path.join(work, 'archives')).filter(file => file.endsWith('.deb'))) {
            const fields = run('dpkg-deb', ['--field', archive]);
            const field = name => fields.match(new RegExp(`^${name}: (.+)$`, 'm'))?.[1];
            packages.push({ package: field('Package'), version: field('Version'),
                source: field('Source') || field('Package'), sha256: sha256(fs.readFileSync(archive)) });
            run('dpkg-deb', ['--extract', archive, root]);
        }
        const libraries = new Map();
        for (const library of filesIn(root)) {
            if (!path.basename(library).includes('.so') || library.includes('/vlc/plugins/')) continue;
            if (fs.existsSync(library) && fs.statSync(library).isFile() && !libraries.has(path.basename(library))) {
                libraries.set(path.basename(library), library);
            }
        }
        const pending = ['libvlc.so.5', 'libvlccore.so.9'].map(name => {
            if (!libraries.has(name)) throw new Error(`Downloaded packages do not contain ${name}.`);
            return [libraries.get(name), path.join(bundle, name)];
        });
        const plugins = path.join(root, 'usr/lib/x86_64-linux-gnu/vlc/plugins');
        const pluginFiles = filesIn(plugins).filter(file => file.endsWith('.so'));
        if (!pluginFiles.some(file => file.includes('avcodec')) || !pluginFiles.some(file => file.includes('avformat'))) {
            throw new Error('Downloaded VLC packages do not contain the required video decoder/demuxer.');
        }
        // libvlccore discovers <its directory>/vlc/plugins through dladdr().
        pending.push(...pluginFiles.map(file => [file, path.join(bundle, 'vlc/plugins', path.relative(plugins, file))]));
        const copied = new Set();
        while (pending.length) {
            const [source, target] = pending.pop();
            if (copied.has(target)) continue;
            copied.add(target);
            fs.mkdirSync(path.dirname(target), { recursive: true });
            fs.copyFileSync(source, target);
            fs.chmodSync(target, 0o755);
            const dependencies = run('readelf', ['--dynamic', source]).matchAll(/\(NEEDED\).*\[(.+?)\]/g);
            for (const [, name] of dependencies) {
                if (HOST_LIBRARIES.has(name)) continue;
                if (!libraries.has(name)) throw new Error(`Missing bundled dependency ${name} required by ${path.basename(source)}`);
                pending.push([libraries.get(name), path.join(bundle, name)]);
            }
            const relative = path.relative(path.dirname(target), bundle);
            run(path.join(root, 'usr/bin/patchelf'), ['--set-rpath', relative ? `$ORIGIN/${relative}` : '$ORIGIN', target]);
        }
        // The managed binding imports 'libvlc', not its versioned SONAME.
        fs.symlinkSync('libvlc.so.5', path.join(bundle, 'libvlc.so'));
        fs.symlinkSync('libvlccore.so.9', path.join(bundle, 'libvlccore.so'));
        const notices = path.join(bundle, 'licenses');
        fs.cpSync(path.join(root, 'usr/share/doc'), path.join(notices, 'packages'), {
            recursive: true, dereference: true, filter: source => fs.existsSync(source),
        });
        const common = path.join(root, 'usr/share/common-licenses');
        if (fs.existsSync(common)) fs.cpSync(common, path.join(notices, 'common-licenses'), { recursive: true });
        fs.writeFileSync(path.join(notices, 'README.txt'),
            'Linux libVLC and dependencies from signed Ubuntu 22.04 (jammy, updates and security) packages.\n' +
            'Exact binary/source package versions and SHA-256 hashes are in ../manifest.json.\n' +
            'Corresponding source packages: https://launchpad.net/ubuntu/jammy/+source/<source-package>\n' +
            'Replace <source-package> with the source name in the manifest (without its version suffix).\n' +
            'Local modification: ELF RUNPATH rewritten relative to each library ($ORIGIN).\n' +
            'glibc and the ELF loader are supplied by the host, not this bundle.\n');
        fs.writeFileSync(path.join(bundle, 'manifest.json'), JSON.stringify({
            builder: fingerprint, version: runtime.version, rid: runtime.rid,
            baseline: runtime.baseline, packageSource: 'Ubuntu 22.04 amd64', packages,
        }, null, 2) + '\n');
        fs.rmSync(destination, { recursive: true, force: true });
        fs.renameSync(bundle, destination);
        console.log(`Bundled Linux libVLC: ${copied.size} libraries/plugins in ${destination}`);
    } finally {
        fs.rmSync(work, { recursive: true, force: true });
    }
}

async function main() {
    const args = process.argv.slice(2);
    if (args[0] !== '--output' || !args[1] || args.length > 3 || (args[2] && args[2] !== '--locked')) {
        throw new Error('Usage: node build-runtime.cjs --output <directory>');
    }
    const destination = path.resolve(args[1]);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    if (args[2] === '--locked') {
        await prepare(destination);
    } else {
        // Kernel lock: concurrent app/smoke-test builds share the cache, and a
        // killed builder cannot leave a stale lock. No shell interpolation.
        const result = spawnSync('flock', ['--exclusive', `${destination}.lock`,
            process.execPath, __filename, '--output', destination, '--locked'], { stdio: 'inherit' });
        if (result.error) throw new Error(`Linux libVLC packaging requires flock (util-linux): ${result.error.message}`);
        process.exitCode = result.status ?? 1;
    }
}

main().catch(error => {
    console.error(`Linux libVLC: ${error.message}`);
    process.exitCode = 1;
});
