import tailwindcss from '@tailwindcss/vite';
import vue from '@vitejs/plugin-vue';
import { colorModeScript } from '@wow-two-beta/fe/foundation/primitives';
import { defineConfig, type Plugin } from 'vite';
import mkcert from 'vite-plugin-mkcert';

/** Holds the build's version: CI passes `APP_VERSION`; a local build is `dev`. */
const version = process.env.APP_VERSION || 'dev';

/**
 * Serves and emits `/theme.js`: the fe sdk's pre-paint colour script for the provider's default storage key, so a
 * dark page never flashes light. A same-origin file rather than an inline script, so a strict CSP needs no hash.
 */
function themeScript(): Plugin {
  const source = colorModeScript();
  return {
    name: 'identity-theme-script',
    configureServer(server) {
      server.middlewares.use('/theme.js', (_request, response) => {
        response.setHeader('Content-Type', 'text/javascript');
        response.end(source);
      });
    },
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'theme.js', source });
    },
  };
}

export default defineConfig(({ command, mode }) => {
  const https = mode !== 'http' && process.env.VITE_HTTPS !== 'false';
  const backend = {
    target: process.env.VITE_API_TARGET ?? 'https://localhost:8388',
    changeOrigin: false,
    secure: false,
  };

  return {
    base: '/',
    define: { __APP_VERSION__: JSON.stringify(version) },
    plugins: [vue(), tailwindcss(), themeScript(), ...(command === 'serve' && https ? [mkcert()] : [])],
    build: { outDir: 'dist', emptyOutDir: true, target: 'es2022' },
    server: {
      host: 'localhost',
      port: 8390,
      strictPort: true,
      https: https ? {} : undefined,
      // The account API, and the OIDC endpoints a sign-in continues to.
      proxy: { '/api': backend, '/connect': backend },
    },
  };
});
