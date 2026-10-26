import js from '@eslint/js';
import vue from 'eslint-plugin-vue';
import globals from 'globals';
import tseslint from 'typescript-eslint';

export default [
  { ignores: ['dist/**', 'node_modules/**', 'coverage/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...vue.configs['flat/recommended'],
  {
    files: ['**/*.{ts,vue}'],
    languageOptions: { globals: globals.browser, parserOptions: { parser: tseslint.parser } },
    rules: {
      'vue/max-attributes-per-line': 'off',
      'vue/singleline-html-element-content-newline': 'off',
      // Prettier writes void elements self-closed; keep the two formatters in agreement.
      'vue/html-self-closing': ['warn', { html: { void: 'always', normal: 'never', component: 'always' } }],
    },
  },
  { files: ['*.mjs', 'scripts/**/*.mjs', '*config.ts'], languageOptions: { globals: globals.node } },
];
