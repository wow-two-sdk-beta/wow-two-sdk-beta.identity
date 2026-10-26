import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [vue()],
  define: { __APP_VERSION__: JSON.stringify('test') },
  test: { environment: 'happy-dom', include: ['tests/**/*.test.ts'], restoreMocks: true },
});
