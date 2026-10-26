import { readFile, readdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { join } from 'node:path';
import { compileScript, compileTemplate, parse } from '@vue/compiler-sfc';

const source = fileURLToPath(new URL('../src/', import.meta.url));
let count = 0;

async function check(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const filename = join(directory, entry.name);
    if (entry.isDirectory()) await check(filename);
    else if (entry.name.endsWith('.vue')) {
      const { descriptor, errors } = parse(await readFile(filename, 'utf8'), { filename });
      if (errors.length) throw new Error(errors.map(String).join('\n'));
      const script = compileScript(descriptor, { id: filename });
      const template = compileTemplate({
        source: descriptor.template?.content ?? '',
        filename,
        id: filename,
        compilerOptions: { bindingMetadata: script.bindings },
      });
      if (template.errors.length) throw new Error(template.errors.map(String).join('\n'));
      count += 1;
    }
  }
}

await check(source);
console.log(`Compiled ${count} Vue single-file components.`);
