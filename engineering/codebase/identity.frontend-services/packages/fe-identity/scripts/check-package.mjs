// Packed-consumer check: packs the package as npm would publish it, installs the archive into a throwaway consumer
// beside its peers, then imports the runtime-safe entries and type-checks every export through the manifest only.
//   node scripts/check-package.mjs [--output <path.tgz>]   keeps the checked archive at <path.tgz> for the publish
import { execFileSync } from 'node:child_process';
import {
  copyFileSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const manifest = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
const outputIndex = process.argv.indexOf('--output');
const output = outputIndex > 0 ? resolve(process.argv[outputIndex + 1]) : null;
const scratch = mkdtempSync(join(tmpdir(), 'fe-domain-package-'));
const run = (command, args, cwd = scratch) =>
  execFileSync(command, args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
const fail = (message) => {
  throw new Error(message);
};

try {
  // The fe sdk peer floor follows the pinned dev version: >=0.0.n <0.1.0 (domain-structure § Core).
  const pinned = manifest.devDependencies?.['@wow-two-beta/fe'];
  const expectedPeer = `>=${pinned} <0.1.0`;
  if (manifest.peerDependencies?.['@wow-two-beta/fe'] !== expectedPeer) {
    fail(`peerDependencies["@wow-two-beta/fe"] must be "${expectedPeer}" to match the pinned ${pinned}`);
  }

  const packed = JSON.parse(
    run(
      'npm',
      ['pack', '--json', '--ignore-scripts', '--cache', join(scratch, 'cache'), '--pack-destination', scratch],
      root,
    ),
  )[0];
  for (const file of packed.files) {
    if (!file.path.startsWith('dist/') && !['package.json', 'README.md', 'LICENSE'].includes(file.path)) {
      fail(`Unexpected published file: ${file.path}`);
    }
  }
  for (const required of ['README.md', 'LICENSE']) {
    if (!packed.files.some((file) => file.path === required)) fail(`The package must ship ${required}`);
  }

  const modules = join(scratch, 'node_modules');
  const packageRoot = join(modules, manifest.name);
  mkdirSync(packageRoot, { recursive: true });
  run('tar', ['-xzf', join(scratch, packed.filename), '--strip-components=1', '-C', packageRoot]);
  for (const peer of Object.keys(manifest.peerDependencies ?? {})) {
    const target = join(modules, peer);
    mkdirSync(dirname(target), { recursive: true });
    symlinkSync(realpathSync(join(root, 'node_modules', peer)), target, 'dir');
  }

  const exportsMap = Object.entries(manifest.exports);
  for (const [key, target] of exportsMap) {
    for (const path of Object.values(target)) {
      if (!existsSync(join(packageRoot, path))) fail(`Missing packed export ${key}: ${path}`);
    }
  }

  // Runtime: the contracts entry has no component or CSS, so plain Node imports it from the archive.
  run(process.execPath, ['--input-type=module', '-e', `await import('${manifest.name}/contracts');`]);

  // Types: every export compiles through the manifest, with no source alias.
  writeFileSync(
    join(scratch, 'consumer.mts'),
    exportsMap
      .map(([key], index) => `import * as Entry${index} from '${manifest.name}${key.slice(1)}';\nvoid Entry${index};`)
      .join('\n'),
  );
  writeFileSync(
    join(scratch, 'tsconfig.json'),
    JSON.stringify({
      compilerOptions: {
        strict: true,
        noEmit: true,
        module: 'ESNext',
        moduleResolution: 'Bundler',
        target: 'ES2022',
        lib: ['ES2022', 'DOM', 'DOM.Iterable'],
        types: [],
        skipLibCheck: true,
      },
      files: ['consumer.mts'],
    }),
  );
  run(process.execPath, [join(root, 'node_modules/typescript/bin/tsc'), '-p', join(scratch, 'tsconfig.json')]);

  if (output) {
    mkdirSync(dirname(output), { recursive: true });
    copyFileSync(join(scratch, packed.filename), output);
  }
  console.log(
    `[check:package] ${manifest.name}@${manifest.version}: ${packed.files.length} files, ${exportsMap.length} exports verified.`,
  );
} finally {
  rmSync(scratch, { recursive: true, force: true });
}
