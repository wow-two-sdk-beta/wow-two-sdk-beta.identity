import { describe, expect, it } from 'vitest';
import { readIdentityCode } from '../src/kit';
import { fakeAccountApi, problem, serviceUrl } from './FakeAccountApi';

const started = { telegramLinkUrl: 'https://t.me/ocharo_bot?start=su-1', expiresAt: '2026-11-02T10:05:00+00:00' };

describe('invitation sign-up client', () => {
  it('starts with a multipart profile, naming the client, and reads the bot link', async () => {
    const { client, fetch, received } = fakeAccountApi({
      'POST /invitations/tok%2F1/sign-up': () => Response.json({ data: started }),
    });
    const picture = new File([new Uint8Array(32)], 'me.png', { type: 'image/png' });

    const result = await client.startInviteSignUp('tok/1', { displayName: 'Lola', picture }, { clientId: 'desk' });

    expect(result).toEqual({ ok: true, value: started });
    expect(received[0]?.url.toString()).toBe(`${serviceUrl}/api/account/invitations/tok%2F1/sign-up?clientId=desk`);
    const body = received[0]?.body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect(body.get('displayName')).toBe('Lola');
    const sent = body.get('picture') as File;
    expect([sent.name, sent.type, sent.size]).toEqual(['me.png', 'image/png', 32]);
    const headers = new Headers(fetch.mock.calls[0]?.[1]?.headers);
    expect(headers.get('Content-Type')).toBeNull();
    expect(fetch.mock.calls[0]?.[1]?.credentials).toBe('include');
  });

  it('sends only the parts given, and no query without a client', async () => {
    const { client, received } = fakeAccountApi({
      'POST /invitations/tok-1/sign-up': () => Response.json({ data: started }),
    });

    await client.startInviteSignUp('tok-1', {});
    await client.startInviteSignUp('tok-1', { displayName: null, picture: null });

    for (const request of received) {
      expect(request.url.search).toBe('');
      expect([...(request.body as FormData).keys()]).toEqual([]);
    }
  });

  it.each([
    ['no link', { expiresAt: started.expiresAt }],
    ['a link that is no web or Telegram link', { ...started, telegramLinkUrl: 'javascript:alert(1)' }],
  ])('refuses a start answer with %s', async (_case, data) => {
    const { client } = fakeAccountApi({ 'POST /invitations/tok-1/sign-up': () => Response.json({ data }) });

    const result = await client.startInviteSignUp('tok-1', {});

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.code).toBe('protocol');
  });

  it("keeps a refused start's identity code", async () => {
    const { client } = fakeAccountApi({
      'POST /invitations/tok-1/sign-up': () =>
        problem(409, 'This invitation has expired.', { identityCode: 'InvitationExpired' }),
    });

    const result = await client.startInviteSignUp('tok-1', {});

    expect(result.ok).toBe(false);
    if (!result.ok)
      expect([result.failure.status, readIdentityCode(result.failure)]).toEqual([409, 'InvitationExpired']);
  });

  it("reads each status of the browser's attempt, naming the client", async () => {
    const answers = [
      { status: 'pending', failureCode: null, isSignedIn: false },
      { status: 'failed', failureCode: 'InvitationIdentifierMismatch', isSignedIn: false },
      { status: 'completed', failureCode: '', isSignedIn: true },
      { status: 'expired' },
    ];
    const { client, received } = fakeAccountApi({
      'GET /invitations/sign-up/status': () => Response.json({ data: answers.shift() }),
    });

    const results = [];
    for (let index = 0; index < 4; index += 1) results.push(await client.getInviteSignUpStatus({ clientId: 'desk' }));

    expect(results).toEqual([
      { ok: true, value: { status: 'pending', failureCode: null, isSignedIn: false } },
      { ok: true, value: { status: 'failed', failureCode: 'InvitationIdentifierMismatch', isSignedIn: false } },
      { ok: true, value: { status: 'completed', failureCode: null, isSignedIn: true } },
      { ok: true, value: { status: 'expired', failureCode: null, isSignedIn: false } },
    ]);
    expect(received[0]?.url.toString()).toBe(`${serviceUrl}/api/account/invitations/sign-up/status?clientId=desk`);
  });

  it('refuses an unknown status and keeps the code of a missing attempt', async () => {
    const answers = [
      () => Response.json({ data: { status: 'lost' } }),
      () => problem(404, 'No sign-up is running.', { identityCode: 'SignUpVerificationNotFound' }),
    ];
    const { client } = fakeAccountApi({ 'GET /invitations/sign-up/status': () => answers.shift()!() });

    const unknown = await client.getInviteSignUpStatus();
    const missing = await client.getInviteSignUpStatus();

    expect(!unknown.ok && unknown.failure.code).toBe('protocol');
    expect(!missing.ok && readIdentityCode(missing.failure)).toBe('SignUpVerificationNotFound');
  });
});
