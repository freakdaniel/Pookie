const fs = require('node:fs');
const path = require('node:path');

const runtime = require('./runtime.json');
if (!/^[1-9][0-9]*$/.test(runtime.version) ||
    runtime.rid !== 'linux-x64' || runtime.asset !== 'libvlc-linux-x64.tar.gz') {
    throw new Error('Invalid Linux libVLC runtime configuration.');
}

function readChecksum(text) {
    const match = text.trim().match(/^([a-f0-9]{64})  (\S+)$/);
    if (!match || match[2] !== runtime.asset) throw new Error('Invalid Linux libVLC checksum file.');
    return match[1];
}

function validateBundle(directory) {
    const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'manifest.json'), 'utf8'));
    if (manifest.version !== runtime.version || manifest.rid !== runtime.rid) {
        throw new Error('Linux libVLC archive version/architecture does not match runtime.json.');
    }
    for (const file of ['libvlc.so', 'libvlc.so.5', 'libvlccore.so', 'libvlccore.so.9',
        'vlc/plugins/codec/libavcodec_plugin.so', 'vlc/plugins/demux/libavformat_plugin.so', 'licenses/README.txt']) {
        if (!fs.statSync(path.join(directory, file)).isFile()) throw new Error(`Missing Linux libVLC runtime file: ${file}`);
    }
}

module.exports = { runtime, readChecksum, validateBundle };

if (require.main === module) {
    try {
        if (process.argv.length === 4 && process.argv[2] === '--checksum') {
            console.log(`sha256=${readChecksum(fs.readFileSync(process.argv[3], 'utf8'))}`);
        } else if (process.argv.length === 2) {
            console.log(`asset=${runtime.asset}\nversion=${runtime.version}`);
        } else {
            throw new Error('Usage: node runtime.cjs [--checksum <file>]');
        }
    } catch (error) { console.error(error.message); process.exitCode = 1; }
}
