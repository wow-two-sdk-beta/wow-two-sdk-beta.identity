import { describe, expect, it, vi } from 'vitest';
import { createAccountClient, readIdentityCode, readTwoFactorChallenge } from '../src/kit';
import { fakeAccountApi, noContent, problem, serviceUrl } from './FakeAccountApi';

describe('account client', () => {
  it('signs in for a cookie and names the client the sign-in serves', async () => {
    const { client, fetch, received } = fakeAccountApi({ 'POST /sign-in': noContent });

    const result = await client.signIn({ identifier: 'ann@example.test', password: 'secret' }, { clientId: 'desk' });

    expect(result).toEqual({ ok: true, value: null });
    expect(received[0]?.url.toString()).toBe(`${serviceUrl}/api/account/sign-in?useCookies=true&clientId=desk`);
    expect(received[0]?.body).toEqual({ identifier: 'ann@example.test', password: 'secret' });
    expect(fetch.mock.calls[0]?.[1]?.credentials).toBe('include');
  });

  it.each([
    ['bare', { isPasswordChangeRequired: true, confirmation: 'Code', ticket: 'tk' }],
    ['enveloped', { data: { isPasswordChangeRequired: true, confirmation: 'Code', ticket: 'tk' } }],
  ])('reads the ticket of a %s required password change from the sign-in answer', async (_shape, body) => {
    const { client } = fakeAccountApi({ 'POST /sign-in': () => Response.json(body) });

    const result = await client.signIn({ identifier: 'ann', password: 'old' });

    expect(result).toEqual({
      ok: true,
      value: { isPasswordChangeRequired: true, confirmation: 'Code', ticket: 'tk' },
    });
  });

  it('reads an invitation link and accepts it for the signed-in person', async () => {
    const preview = {
      organizationName: 'Ocharo',
      role: 'editor',
      status: 'pending',
      phoneNumberHint: '+998*******67',
      emailHint: null,
      expiresAt: '2026-11-09T07:00:00+00:00',
    };
    const { client, received } = fakeAccountApi({
      'GET /invitations/tok-1': () => Response.json({ data: preview }),
      'POST /invitations/tok-1/accept': noContent,
    });

    expect(await client.getInvitation('tok-1')).toEqual({ ok: true, value: { ...preview, trustedContactHint: null } });
    expect(await client.acceptInvitation('tok-1')).toEqual({ ok: true, value: undefined });
    expect(received.map((request) => `${request.method} ${request.url.pathname}`)).toEqual([
      'GET /api/account/invitations/tok-1',
      'POST /api/account/invitations/tok-1/accept',
    ]);
  });

  it('refuses an invitation preview with an unknown status', async () => {
    const { client } = fakeAccountApi({
      'GET /invitations/tok-2': () =>
        Response.json({ data: { organizationName: 'Ocharo', role: 'editor', status: 'lost' } }),
    });

    const result = await client.getInvitation('tok-2');

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.code).toBe('protocol');
  });

  it('refuses a required password change without its ticket', async () => {
    const { client } = fakeAccountApi({
      'POST /sign-in': () => Response.json({ data: { isPasswordChangeRequired: true, confirmation: 'None' } }),
    });

    const result = await client.signIn({ identifier: 'ann', password: 'old' });

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.code).toBe('protocol');
  });

  it('keeps the problem of a refused sign-in', async () => {
    const { client } = fakeAccountApi({
      'POST /sign-in': () => problem(401, 'The user name, email or password is incorrect.'),
    });

    const result = await client.signIn({ identifier: 'ann', password: 'wrong' });

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.problem?.detail).toBe('The user name, email or password is incorrect.');
  });

  it('completes a ticketed second factor for a cookie, naming the client', async () => {
    const { client, received } = fakeAccountApi({ 'POST /sign-in/two-factor': noContent });

    const result = await client.signInWithSecondFactor(
      { userId: 'u1', ticket: 'tk', twoFactorCode: '123456', isPersistent: true },
      { clientId: 'desk' },
    );

    expect(result).toEqual({ ok: true, value: undefined });
    expect(received[0]?.url.toString()).toBe(
      `${serviceUrl}/api/account/sign-in/two-factor?useCookies=true&clientId=desk`,
    );
    expect(received[0]?.body).toEqual({ userId: 'u1', ticket: 'tk', twoFactorCode: '123456', isPersistent: true });
  });

  it("reads a refusal's identity code and second-factor challenge", async () => {
    const answers = [
      problem(403, 'Confirm your email to sign in.', { identityCode: 'EmailConfirmationRequired' }),
      problem(401, 'Enter your two-factor code.', { twoFactorMethod: 'authenticator' }),
      problem(401, 'Enter your two-factor code.', {
        identityCode: 'TwoFactorRequired',
        twoFactorMethod: 'email',
        userId: 'u1',
        twoFactorTicket: 'tk',
      }),
      problem(401, 'The user name, email or password is incorrect.', { identityCode: 'SignInFailed' }),
      problem(429, 'A code was sent moments ago.', { identityCode: 'TwoFactorRequired' }),
    ];
    const { client } = fakeAccountApi({ 'POST /sign-in': () => answers.shift()! });
    const failures = [];
    for (let index = 0; index < 5; index += 1) {
      const result = await client.signIn({ identifier: 'ann', password: 'secret' });
      if (!result.ok) failures.push(result.failure);
    }

    expect(failures.map(readIdentityCode)).toEqual([
      'EmailConfirmationRequired',
      null,
      'TwoFactorRequired',
      'SignInFailed',
      'TwoFactorRequired',
    ]);
    expect(failures.map(readTwoFactorChallenge)).toEqual([
      null,
      { twoFactorMethod: 'authenticator', userId: null, twoFactorTicket: null },
      { twoFactorMethod: 'email', userId: 'u1', twoFactorTicket: 'tk' },
      null,
      null,
    ]);
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
