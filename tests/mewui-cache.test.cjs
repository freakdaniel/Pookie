const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

test('MewUI cache fingerprints every packing policy input, independently of its directory', t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'pookie-mewui-policy-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const repository = path.resolve(__dirname, '..');
  const artifacts = path.join(root, 'build');
  const built = spawnSync('dotnet', ['build', path.join(repository, 'tools/mewui-source/MewUiSource.csproj'),
    '--artifacts-path', artifacts, '-c', 'Release', '--verbosity', 'quiet'], { encoding: 'utf8', timeout: 60_000 });
  assert.equal(built.status, 0, built.stdout + built.stderr);
  const tool = path.join(artifacts, 'bin/MewUiSource/release/MewUiSource.dll');
  const first = path.join(root, 'first'), second = path.join(root, 'second');
  fs.mkdirSync(first); fs.mkdirSync(second);
  const inputs = ['Program.cs', 'revision.json', 'MewUiSource.csproj', 'MewUiPack.targets', 'MewUiSource.targets', 'build.ps1'];
  for (const file of inputs) {
    fs.writeFileSync(path.join(first, file), `original ${file}\n`);
    fs.copyFileSync(path.join(first, file), path.join(second, file));
  }
  function fingerprint(directory) {
    const run = spawnSync('dotnet', [tool, '--fingerprint', directory], { encoding: 'utf8', timeout: 10_000 });
    assert.equal(run.status, 0, run.stdout + run.stderr);
    assert.match(run.stdout.trim(), /^[a-f0-9]{64}$/);
    return run.stdout.trim();
  }
  const original = fingerprint(first);
  assert.equal(original, fingerprint(second));
  for (const file of inputs) {
    const target = path.join(first, file), previous = fs.readFileSync(target);
    fs.appendFileSync(target, 'packing-policy change');
    assert.notEqual(original, fingerprint(first), `${file} must invalidate the package cache`);
    fs.writeFileSync(target, previous);
  }
  fs.writeFileSync(path.join(first, 'NewPolicy.cs'), 'new helper');
  assert.notEqual(original, fingerprint(first));
});
