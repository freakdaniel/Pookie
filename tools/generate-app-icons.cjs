#!/usr/bin/env node
// SPDX-FileCopyrightText: 2026 Daniel Freak
// SPDX-License-Identifier: BSD-3-Clause
const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const assets = path.resolve(__dirname, '../src/Pookie.App/Assets/Branding');
const sizes = [16, 24, 32, 48, 64, 128, 256, 512];
const png = size => path.join(assets, `app-icon-${size}.png`);

// GJS exposes the Linux system SVG loader through GObject introspection.
const render = `
    const { GdkPixbuf } = imports.gi;
    const [assets, ...sizes] = ARGV;
    for (const value of sizes) {
        const size = Number(value);
        const bitmap = GdkPixbuf.Pixbuf.new_from_file_at_scale(assets + '/app-icon.svg', size, size, true);
        bitmap.savev(assets + '/app-icon-' + size + '.png', 'png', [], []);
    }
`;

try {
    execFileSync('gjs', ['-c', render, assets, ...sizes.map(String)], { stdio: 'inherit' });
    const frames = sizes.filter(size => size <= 256);
    const directory = Buffer.alloc(6 + 16 * frames.length);
    directory.writeUInt16LE(1, 2); // ICO
    directory.writeUInt16LE(frames.length, 4);
    const images = [];
    let offset = directory.length;
    for (const [index, size] of frames.entries()) {
        const bytes = fs.readFileSync(png(size));
        const entry = 6 + index * 16;
        directory[entry] = directory[entry + 1] = size === 256 ? 0 : size;
        directory.writeUInt16LE(1, entry + 4); // color planes
        directory.writeUInt16LE(32, entry + 6);
        directory.writeUInt32LE(bytes.length, entry + 8);
        directory.writeUInt32LE(offset, entry + 12);
        images.push(bytes);
        offset += bytes.length;
    }
    fs.writeFileSync(path.join(assets, 'app-icon.ico'), Buffer.concat([directory, ...images]));
    console.log(`Generated PNG sizes and ICO in ${assets}`);
} catch (error) {
    console.error(error.code === 'ENOENT' && error.path === 'gjs'
        ? 'Install GJS and the GdkPixbuf SVG loader to render the app icons.' : error.message);
    process.exitCode = 1;
}
