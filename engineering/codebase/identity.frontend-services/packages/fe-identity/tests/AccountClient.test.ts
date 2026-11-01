import { describe, expect, it, vi } from 'vitest';
import { createAccountClient } from '../src/kit';
import { fakeAccountApi, noContent, problem, serviceUrl } from './FakeAccountApi';

describe('account client', () => {
  it('signs in for a cookie and names the client the sign-in serves', async () => {
    const { client, fetch, received } = fakeAccountApi({ 'POST /login': noContent });

    const result = await client.signIn({ login: 'ann@example.test', password: 'secret' }, { clientId: 'desk' });

    expect(result).toEqual({ ok: true, value: null });
    expect(received[0]?.url.toString()).toBe(`${serviceUrl}/api/account/login?useCookies=true&clientId=desk`);
    expect(received[0]?.body).toEqual({ login: 'ann@example.test', password: 'secret' });
    expect(fetch.mock.calls[0]?.[1]?.credentials).toBe('include');
  });

  it.each([
    ['bare', { isPasswordChangeRequired: true, confirmation: 'Code', ticket: 'tk' }],
    ['enveloped', { data: { isPasswordChangeRequired: true, confirmation: 'Code', ticket: 'tk' } }],
  ])('reads the ticket of a %s required password change from the sign-in answer', async (_shape, body) => {
    const { client } = fakeAccountApi({ 'POST /login': () => Response.json(body) });

    const result = await client.signIn({ login: 'ann', password: 'old' });

    expect(result).toEqual({
      ok: true,
      value: { isPasswordChangeRequired: true, confirmation: 'Code', ticket: 'tk' },
    });
  });

  it('refuses a required password change without its ticket', async () => {
    const { client } = fakeAccountApi({
      'POST /login': () => Response.json({ data: { isPasswordChangeRequired: true, confirmation: 'None' } }),
    });

    const result = await client.signIn({ login: 'ann', password: 'old' });

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.code).toBe('protocol');
  });

  it('keeps the problem of a refused sign-in', async () => {
    const { client } = fakeAccountApi({ 'POST /login': () => problem(401, 'The login or password is incorrect.') });

    const result = await client.signIn({ login: 'ann', password: 'wrong' });

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.problem?.detail).toBe('The login or password is incorrect.');
  });

  it('decodes the password change confirmation, whatever the case of its type', async () => {
    const { client, received } = fakeAccountApi({
      'POST /manage/password/confirmation': () => Response.json({ data: { confirmation: 'code', isSent: true } }),
    });

    expect(await client.startPasswordChange()).toEqual({ ok: true, value: { confirmation: 'Code', isSent: true } });
    await client.startPasswordChange({ ticket: 'tk' });
    expect(received.map((request) => request.body)).toEqual([{}, { ticket: 'tk' }]);
  });

  it('decodes a phone code answer with and without a Telegram link', async () => {
    const answers = [
      { isCodeSent: true, telegramLinkUrl: null },
      { isCodeSent: false, telegramLinkUrl: 'https://t.me/ocharo_bot?start=abc' },
    ];
    const { client } = fakeAccountApi({ 'POST /phone-sign-in/send': () => Response.json({ data: answers.shift() }) });

    expect(await client.sendPhoneSignInCode({ phone: '+998901234567' })).toEqual({
      ok: true,
      value: { isCodeSent: true, telegramLinkUrl: null },
    });
    expect(await client.sendPhoneSignInCode({ phone: '+998901234567' })).toEqual({
      ok: true,
      value: { isCodeSent: false, telegramLinkUrl: 'https://t.me/ocharo_bot?start=abc' },
    });
  });

  it('confirms an email through the query and leaves same-origin calls on the default credentials', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => new Response(null, { status: 204 }));
    const client = createAccountClient({ fetch });

    await client.confirmEmail({ userId: 'u1', token: 'a+b/c' });

    expect(String(fetch.mock.calls[0]?.[0])).toBe('/api/account/confirm-email?userId=u1&token=a%2Bb%2Fc');
    expect(fetch.mock.calls[0]?.[1]?.credentials).toBeUndefined();
  });
});
