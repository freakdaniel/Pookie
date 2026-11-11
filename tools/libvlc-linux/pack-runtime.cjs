#!/usr/bin/env node
const { spawnSync } = require('node:child_process');
const { createHash } = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { runtime, validateBundle } = require('./runtime.cjs');

try {
    const args = process.argv.slice(2);
    if (args.length !== 4 || args[0] !== '--input' || args[2] !== '--output') {
        throw new Error('Usage: node pack-runtime.cjs --input <runtime-directory> --output <archive-directory>');
    }
    const input = path.resolve(args[1]);
    const output = path.resolve(args[3]);
    if (output === input || output.startsWith(input + path.sep)) throw new Error('Archive output must be outside the runtime directory.');
    validateBundle(input);
    fs.mkdirSync(output, { recursive: true });
    const archive = path.join(output, runtime.asset);
    // Stable metadata, ordering and gzip header; the same runtime produces the
    // same archive regardless of workspace paths, uid or build time.
    const result = spawnSync('tar', ['--create', '--use-compress-program=gzip -n', '--format=gnu',
        '--sort=name', '--mtime=@0', '--owner=0', '--group=0', '--numeric-owner',
        '--file', archive, '--directory', input, '.'], { stdio: 'inherit' });
    if (result.error || result.status !== 0) throw new Error(result.error?.message || 'Could not create libVLC archive.');
    const sha256 = createHash('sha256').update(fs.readFileSync(archive)).digest('hex');
    fs.writeFileSync(`${archive}.sha256`, `${sha256}  ${runtime.asset}\n`);
    console.log(`Packaged ${archive}\nSHA-256: ${sha256}`);
} catch (error) { console.error(error.message); process.exitCode = 1; }
