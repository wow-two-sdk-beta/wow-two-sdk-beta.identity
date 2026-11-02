import vue from '@vitejs/plugin-vue';
import { readFileSync } from 'node:fs';
import { defineConfig } from 'vite';

const manifest = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8')) as {
  peerDependencies?: Record<string, string>;
};

/** Peers stay external, subpaths included, so the consumer's single copy of vue and the fe sdk is used. */
const peers = Object.keys(manifest.peerDependencies ?? {});
const isExternal = (id: string) => peers.some((name) => id === name || id.startsWith(`${name}/`));

/** Library build: one entry per public export; declarations come from vue-tsc (tsconfig.build.json). */
export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: true,
    minify: false,
    lib: {
      entry: {
        'contracts/index': 'src/contracts/index.ts',
        'kit/index': 'src/kit/index.ts',
        'admin/index': 'src/admin/index.ts',
      },
      formats: ['es'],
    },
    rollupOptions: {
      external: isExternal,
      output: { entryFileNames: '[name].js', chunkFileNames: 'chunks/[name]-[hash].js' },
    },
  },
});
