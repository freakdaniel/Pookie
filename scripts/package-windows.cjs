const { execFileSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

function packageWindows(args) {
    const [version, source, destination] = args;
    if (args.length !== 3 || !/^\d+\.\d+\.\d+$/.test(version)) {
        throw new Error('Usage: node scripts/package-windows.cjs <major.minor.patch> <publish directory> <output directory>');
    }
    const publish = path.resolve(source);
    const output = path.resolve(destination);
    if (output === publish || output.startsWith(publish + path.sep)) {
        throw new Error('The output directory must be outside the Windows publish directory.');
    }
    for (const name of ['Pookie.exe', 'Pookie.dll', 'licenses/THIRD-PARTY-NOTICES.txt']) {
        if (!fs.statSync(path.join(publish, name), { throwIfNoEntry: false })?.isFile()) {
            throw new Error(`Incomplete Windows publish directory: missing ${name}`);
        }
    }
    if (['pookie.portable', 'Data'].some(name => fs.existsSync(path.join(publish, name)))) {
        throw new Error('Package a clean publish directory without portable user data.');
    }

    fs.mkdirSync(output, { recursive: true });
    const staging = fs.mkdtempSync(path.join(output, '.pookie-windows-'));
    const installer = `Pookie-${version}-win-x64.msi`;
    const portable = `Pookie-${version}-win-x64-portable.zip`;
    try {
        const config = path.resolve(__dirname, '../packaging/windows/nfpm.yaml');
        execFileSync('nfpm', ['package', '--config', config, '--packager', 'msi', '--target', path.join(staging, installer)], {
            cwd: publish, stdio: 'inherit', env: { ...process.env, VERSION: version, TMPDIR: staging, TMP: staging, TEMP: staging },
        });

        const zipRoot = path.join(staging, 'win-x64');
        fs.cpSync(publish, zipRoot, { recursive: true, mode: fs.constants.COPYFILE_FICLONE });
        fs.writeFileSync(path.join(zipRoot, 'pookie.portable'), '');
        const options = { cwd: staging, stdio: 'inherit' };
        if (process.platform === 'win32') {
            const tar = path.join(process.env.SystemRoot, 'System32', 'tar.exe');
            execFileSync(tar, ['-caf', portable, 'win-x64'], options);
        } else {
            execFileSync('zip', ['-qr', portable, 'win-x64'], options);
        }
        for (const name of [installer, portable]) {
            fs.renameSync(path.join(staging, name), path.join(output, name));
        }
    } finally {
        fs.rmSync(staging, { recursive: true, force: true });
    }
}

try {
    packageWindows(process.argv.slice(2));
} catch (error) {
    console.error(error.message);
    process.exitCode = 1;
}
