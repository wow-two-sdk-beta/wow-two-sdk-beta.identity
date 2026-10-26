import { ESLint } from 'eslint';
import { describe, expect, it } from 'vitest';

const eslint = new ESLint();

describe('application boundaries', () => {
  it('rejects a presentation import from application orchestration', async () => {
    const [result] = await eslint.lintText(
      "import Page from '../../presentation/console/pages/ConsolePage.vue'; void Page;",
      { filePath: 'src/application/system/Invalid.ts' },
    );
    expect(result?.messages.some((message) => message.ruleId === 'no-restricted-imports')).toBe(true);
  });

  it('allows the system integration seam', async () => {
    const [result] = await eslint.lintText("import { systemApi } from '../../integration/system'; void systemApi;", {
      filePath: 'src/application/system/Valid.ts',
    });
    expect(result?.messages).toEqual([]);
  });
});
