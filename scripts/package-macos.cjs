const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..');

function stageApp(version, rid, publish, diskRoot) {
    if (!/^\d+\.\d+\.\d+$/.test(version) || !['osx-x64', 'osx-arm64'].includes(rid)) {
        throw new Error('Expected a numeric release version and a supported macOS RID.');
    }
    const deps = JSON.parse(fs.readFileSync(path.join(publish, 'Pookie.deps.json'), 'utf8'));
    if (!deps.runtimeTarget.name.endsWith('/' + rid) || !deps.libraries['Pookie/' + version]) {
        throw new Error(`Publish directory does not contain Pookie ${version} for ${rid}.`);
    }
    fs.accessSync(path.join(publish, 'Pookie'), fs.constants.X_OK);
    fs.accessSync(path.join(publish, 'licenses', 'THIRD-PARTY-NOTICES.txt'));

    const app = path.join(diskRoot, 'Pookie.app');
    const contents = path.join(app, 'Contents');
    const resources = path.join(contents, 'Resources');
    fs.mkdirSync(resources, { recursive: true });
    fs.cpSync(publish, path.join(contents, 'MacOS'), { recursive: true, verbatimSymlinks: true });
    const plist = fs.readFileSync(path.join(root, 'packaging/macos/Info.plist'), 'utf8');
    fs.writeFileSync(path.join(contents, 'Info.plist'), plist.replaceAll('@VERSION@', version));

    // ICNS accepts PNG representations directly. Keep the original raster artwork at each size.
    const layers = [['icp4', 16], ['icp5', 32], ['ic11', 32], ['ic12', 64], ['ic07', 128],
        ['ic08', 256], ['ic13', 256], ['ic09', 512], ['ic14', 512]];
    const chunks = [];
    for (const [type, size] of layers) {
        const png = fs.readFileSync(path.join(root, 'src/Pookie.App/Assets/Branding', `app-icon-${size}.png`));
        const header = Buffer.alloc(8);
        header.write(type);
        header.writeUInt32BE(header.length + png.length, 4);
        chunks.push(header, png);
    }
    const images = Buffer.concat(chunks);
    const header = Buffer.from('icns\0\0\0\0', 'ascii');
    header.writeUInt32BE(header.length + images.length, 4);
    fs.writeFileSync(path.join(resources, 'Pookie.icns'), Buffer.concat([header, images]));
    fs.symlinkSync('/Applications', path.join(diskRoot, 'Applications'));
    return app;
}

function packageMac(version, rid, source, destination) {
    const publish = path.resolve(source);
    const output = path.resolve(destination);
    const run = (tool, args) => execFileSync(tool, args, { stdio: 'inherit' });
    fs.mkdirSync(output, { recursive: true });
    const work = fs.mkdtempSync(path.join(output, '.pookie-macos-'));
    const diskRoot = path.join(work, 'volume');
    const mount = path.join(work, 'mounted');
    const name = `Pookie-${version}-${rid}.dmg`;
    try {
        const app = stageApp(version, rid, publish, diskRoot);
        const executable = path.join(app, 'Contents/MacOS/Pookie');
        run('lipo', [executable, '-verify_arch', rid === 'osx-arm64' ? 'arm64' : 'x86_64']);
        run('plutil', ['-lint', path.join(app, 'Contents/Info.plist')]);
        run('codesign', ['--force', '--deep', '--sign', '-', app]);
        const dmg = path.join(work, name);
        run('hdiutil', ['create', '-volname', 'Pookie', '-fs', 'HFS+', '-format', 'UDZO', '-srcfolder', diskRoot, dmg]);
        run('hdiutil', ['verify', dmg]);
        fs.mkdirSync(mount);
        run('hdiutil', ['attach', '-readonly', '-nobrowse', '-mountpoint', mount, dmg]);
        try {
            const installed = path.join(mount, 'Pookie.app');
            run('codesign', ['--verify', '--deep', '--strict', installed]);
            const actual = execFileSync(path.join(installed, 'Contents/MacOS/Pookie'), ['--version'], { encoding: 'utf8' }).trim();
            if (actual !== version || fs.readlinkSync(path.join(mount, 'Applications')) !== '/Applications') {
                throw new Error('Mounted DMG failed its version or Applications shortcut check.');
            }
        } finally {
            run('hdiutil', ['detach', mount]);
        }
        fs.renameSync(dmg, path.join(output, name));
    } finally {
        fs.rmSync(work, { recursive: true, force: true });
    }
}

if (require.main === module) {
    try {
        if (process.argv.length !== 6 || process.platform !== 'darwin') {
            throw new Error('Run on macOS: node scripts/package-macos.cjs <version> <rid> <publish directory> <output directory>');
        }
        packageMac(...process.argv.slice(2));
    } catch (error) {
        console.error(error.message);
        process.exitCode = 1;
    }
}

module.exports = { stageApp };
