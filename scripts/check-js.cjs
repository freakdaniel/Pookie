const { spawnSync } = require('node:child_process');
const { readdirSync } = require('node:fs');
const { join, extname } = require('node:path');
function check(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
        if (['node_modules', 'bin', 'obj'].includes(entry.name)) continue;
        const path = join(directory, entry.name);
        if (entry.isDirectory()) check(path);
        else if (['.js', '.cjs'].includes(extname(path))) {
            const result = spawnSync(process.execPath, ['--check', path], { stdio: 'inherit' });
            if (result.status !== 0) process.exit(result.status || 1);
        }
    }
}
for (const directory of ['scripts', 'tests', 'src/Pookie.App/Browser/Scripts']) check(directory);
