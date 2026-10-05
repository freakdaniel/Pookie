const { spawnSync } = require('node:child_process');
const { copyFileSync, existsSync, lstatSync, mkdirSync, mkdtempSync, rmSync } = require('node:fs');
const { tmpdir } = require('node:os');
const { dirname, join, resolve, sep } = require('node:path');

const root = resolve(__dirname, '..');
const listing = spawnSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], {
    cwd: root, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024,
});
if (listing.error || listing.status !== 0) {
    console.error(listing.error?.message || listing.stderr);
    process.exit(1);
}

// REUSE requires LICENSES/. Adapt only the validation copy, keeping licenses/ in the project.
const staging = mkdtempSync(join(tmpdir(), 'pookie-reuse-'));
let status = 1;
try {
    for (const file of new Set(listing.stdout.split('\0').filter(Boolean))) {
        const source = resolve(root, file);
        const target = resolve(staging, file.replace(/^licenses\//, 'LICENSES/'));
        if (!source.startsWith(root + sep) || !target.startsWith(staging + sep)) {
            throw new Error(`File path escapes validation workspace: ${file}`);
        }
        // Deleted tracked files remain in git ls-files until staged.
        if (!existsSync(source)) continue;
        const info = lstatSync(source);
        if (info.isSymbolicLink()) continue; // REUSE excludes symlinks.
        if (!info.isFile()) throw new Error(`Unsupported repository entry: ${file}`);
        mkdirSync(dirname(target), { recursive: true });
        copyFileSync(source, target);
    }
    const result = spawnSync('uvx', ['--from', 'reuse[charset-normalizer]==6.2.0',
        'reuse', '--root', staging, 'lint'], { cwd: staging, stdio: 'inherit' });
    if (result.error) console.error(result.error.message);
    status = result.status ?? 1;
} finally {
    const tempRoot = resolve(tmpdir());
    if (dirname(staging) !== tempRoot || !staging.startsWith(join(tempRoot, 'pookie-reuse-'))) {
        throw new Error('Refusing to remove a path outside the validation temporary directory.');
    }
    rmSync(staging, { recursive: true, force: true });
}
process.exitCode = status;
