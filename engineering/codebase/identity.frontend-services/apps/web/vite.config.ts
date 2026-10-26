import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vite';
import mkcert from 'vite-plugin-mkcert';

/** Holds the build's version: CI passes `APP_VERSION`; a local build is `dev`. */
const version = process.env.APP_VERSION || 'dev';

export default defineConfig(({ command, mode }) => {
  const https = mode !== 'http' && process.env.VITE_HTTPS !== 'false';

  return {
    base: '/',
    define: { __APP_VERSION__: JSON.stringify(version) },
    plugins: [vue(), tailwindcss(), ...(command === 'serve' && https ? [mkcert()] : [])],
    build: { outDir: 'dist', emptyOutDir: true, target: 'es2022' },
    server: {
      host: 'localhost',
      port: 8390,
      strictPort: true,
      https: https ? {} : undefined,
      proxy: {
        '/api': {
          target: process.env.VITE_API_TARGET ?? 'https://localhost:8388',
          changeOrigin: false,
          secure: false,
        },
      },
    },
  };
});
