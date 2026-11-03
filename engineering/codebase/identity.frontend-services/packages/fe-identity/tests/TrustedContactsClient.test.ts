import { describe, expect, it } from 'vitest';
import { IdentityErrorCodeConstants } from '../src/contracts';
import { isIdentityCode, readIdentityCode } from '../src/kit';
import { fakeAccountApi, noContent, problem, serviceUrl } from './FakeAccountApi';

const choice = {
  token: 'choice-1',
  accounts: [
    { userId: 'u1', displayName: 'Anvar', pictureUrl: null, isSelf: true },
    { userId: 'u2', displayName: 'Lola', pictureUrl: '/api/files/lola.png', isSelf: false },
  ],
};

describe('trusted contact sign-in client', () => {
  it('lists the contacts a phone’s code can go to, and reads none as an empty list', async () => {
    const answers = [
      { trustedContacts: [{ id: 't1', label: 'L*** K.' }] },
      { trustedContacts: [] },
      {},
      { trustedContacts: [{ id: 't1', label: 'L*** K.' }, { label: 'no id' }] },
    ];
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/targets': () => Response.json({ data: answers.shift() }),
    });

    const results = [];
    for (let index = 0; index < 4; index += 1) {
      results.push(await client.getPhoneSignInTargets({ phone: '+998901234567' }));
    }

    expect(results.slice(0, 3)).toEqual([
      { ok: true, value: { trustedContacts: [{ id: 't1', label: 'L*** K.' }] } },
      { ok: true, value: { trustedContacts: [] } },
      { ok: true, value: { trustedContacts: [] } },
    ]);
    expect(!results[3]?.ok && results[3]?.failure.code).toBe('protocol');
    expect(received[0]).toMatchObject({ method: 'POST', body: { phone: '+998901234567' } });
    expect(received[0]?.url.toString()).toBe(`${serviceUrl}/api/account/phone-sign-in/targets`);
  });

  it('sends a code to the contact a target id names', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } }),
    });

    await client.sendPhoneSignInCode({ phone: '+998901234567', targetId: 't1' });

    expect(received[0]?.body).toEqual({ phone: '+998901234567', targetId: 't1' });
  });

  it('signs in with a 204, or reads the account choice a trusted contact gets, enveloped or bare', async () => {
    const answers = [
      () => noContent(),
      () => Response.json({ data: { accountChoice: choice } }),
      () => Response.json({ accountChoice: choice }),
      () => Response.json({ data: {} }),
    ];
    const { client, received } = fakeAccountApi({ 'POST /phone-sign-in': () => answers.shift()!() });
    const request = { phone: '+998901234567', code: '123456', isPersistent: false };

    const results = [];
    for (let index = 0; index < 4; index += 1)
      results.push(await client.signInWithPhone(request, { clientId: 'desk' }));

    expect(results).toEqual([
      { ok: true, value: null },
      { ok: true, value: choice },
      { ok: true, value: choice },
      { ok: true, value: null },
    ]);
    expect(received[0]?.url.search).toBe('?useCookies=true&clientId=desk');
  });

  it.each([
    ['no accounts', { token: 'c', accounts: [] }],
    ['no token', { accounts: choice.accounts }],
    ['an account without an id', { token: 'c', accounts: [{ displayName: 'Lola', isSelf: false }] }],
  ])('never takes a choice with %s for a sign-in', async (_case, accountChoice) => {
    const { client } = fakeAccountApi({ 'POST /phone-sign-in': () => Response.json({ data: { accountChoice } }) });

    const result = await client.signInWithPhone({ phone: '+998901234567', code: '123456' });

    expect(!result.ok && result.failure.code).toBe('protocol');
  });

  it('shows only a same-origin or HTTPS picture of an account', async () => {
    const accounts = [
      '/p.png',
      'https://cdn.example.test/p.png',
      'http://cdn.example.test/p.png',
      '//evil.test/p.png',
      'javascript:1',
    ].map((pictureUrl, index) => ({ userId: `u${index}`, displayName: null, pictureUrl, isSelf: false }));
    const { client } = fakeAccountApi({
      'POST /phone-sign-in': () => Response.json({ data: { accountChoice: { token: 'c', accounts } } }),
    });

    const result = await client.signInWithPhone({ phone: '+998901234567', code: '123456' });

    expect(result.ok && result.value?.accounts.map((account) => account.pictureUrl)).toEqual([
      '/p.png',
      'https://cdn.example.test/p.png',
      null,
      null,
      null,
    ]);
  });

  it('keeps a refused phone sign-in’s problem', async () => {
    const { client } = fakeAccountApi({
      'POST /phone-sign-in': () => problem(401, 'The code is incorrect.', { identityCode: 'InvalidCode' }),
    });

    const result = await client.signInWithPhone({ phone: '+998901234567', code: '000000' });

    expect(!result.ok && readIdentityCode(result.failure)).toBe('InvalidCode');
  });

  it('signs in as the picked account, by cookie, naming the client', async () => {
    const { client, received } = fakeAccountApi({ 'POST /phone-sign-in/choose': noContent });

    const result = await client.choosePhoneSignInAccount(
      { token: 'choice-1', userId: 'u2', isPersistent: true },
      { clientId: 'desk' },
    );

    expect(result).toEqual({ ok: true, value: undefined });
    expect(received[0]?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(received[0]?.body).toEqual({ token: 'choice-1', userId: 'u2', isPersistent: true });
  });

  it('finishes an invitation sign-up with the trusted contact’s code, naming the client', async () => {
    const { client, received } = fakeAccountApi({ 'POST /invitations/sign-up/verify': noContent });

    const result = await client.verifyInviteSignUp({ code: '123456', isPersistent: false }, { clientId: 'desk' });

    expect(result).toEqual({ ok: true, value: undefined });
    expect(received[0]?.url.toString()).toBe(`${serviceUrl}/api/account/invitations/sign-up/verify?clientId=desk`);
    expect(received[0]?.body).toEqual({ code: '123456', isPersistent: false });
  });
});

describe('trusted contact settings client', () => {
  it('reads the trusted contacts and revokes one by its encoded id', async () => {
    const contact = {
      userId: 'u 2',
      displayName: 'Lola',
      phoneHint: '+998*******45',
      isDeliverable: true,
      createdAt: '2026-11-03T08:00:00+00:00',
    };
    const { client, received } = fakeAccountApi({
      'GET /manage/trusted-contacts': () =>
        Response.json({ data: { canRevoke: true, items: [contact, { userId: 'u3' }] } }),
      'DELETE /manage/trusted-contacts/u%202': noContent,
    });

    const listed = await client.getTrustedContacts();
    const revoked = await client.revokeTrustedContact('u 2');

    expect(listed).toEqual({
      ok: true,
      value: {
        canRevoke: true,
        items: [contact, { userId: 'u3', displayName: null, phoneHint: null, isDeliverable: false, createdAt: '' }],
      },
    });
    expect(revoked.ok).toBe(true);
    expect(received[1]).toMatchObject({ method: 'DELETE' });
    expect(received[1]?.url.pathname).toBe('/api/account/manage/trusted-contacts/u%202');
  });

  it('reads either spelling of the own-phone refusal', async () => {
    const answers = ['main_phone_required', 'MainPhoneRequired', 'SomethingElse'];
    const { client } = fakeAccountApi({
      'DELETE /manage/trusted-contacts/u2': () =>
        problem(403, 'Sign in with your own phone.', { identityCode: answers.shift() }),
    });

    const failures = [];
    for (let index = 0; index < 3; index += 1) {
      const result = await client.revokeTrustedContact('u2');
      if (!result.ok) failures.push(result.failure);
    }

    expect(failures.map((failure) => isIdentityCode(failure, IdentityErrorCodeConstants.mainPhoneRequired))).toEqual([
      true,
      true,
      false,
    ]);
    expect(isIdentityCode(null, IdentityErrorCodeConstants.mainPhoneRequired)).toBe(false);
  });

  it('reads the accounts that trust the person and leaves one, keeping the last contact’s refusal', async () => {
    const account = { userId: 'u1', displayName: 'Anvar', createdAt: '2026-11-03T08:00:00+00:00', canLeave: false };
    const { client } = fakeAccountApi({
      'GET /manage/trusted-by': () => Response.json({ data: [account, { userId: 'u4', canLeave: true }] }),
      'DELETE /manage/trusted-by/u1': () =>
        problem(409, 'You are the last contact.', { identityCode: 'last_trusted_contact' }),
      'DELETE /manage/trusted-by/u4': noContent,
    });

    const listed = await client.getTrustedBy();
    const refused = await client.leaveTrustedContact('u1');
    const left = await client.leaveTrustedContact('u4');

    expect(listed).toEqual({
      ok: true,
      value: [account, { userId: 'u4', displayName: null, createdAt: '', canLeave: true }],
    });
    expect(!refused.ok && isIdentityCode(refused.failure, IdentityErrorCodeConstants.lastTrustedContact)).toBe(true);
    expect(left.ok).toBe(true);
  });

  it('reads the own phone and the bot link that adds or changes it', async () => {
    const link = 'https://t.me/ocharo_bot?start=ap-1';
    const answers = [
      { isConfirmed: false, canChange: true },
      { phoneHint: '+998*******45', isConfirmed: true },
    ];
    const { client, received } = fakeAccountApi({
      'GET /manage/phone': () => Response.json({ data: answers.shift() }),
      'POST /manage/phone/telegram-link': () => Response.json({ data: { telegramLinkUrl: link } }),
    });

    expect(await client.getPhone()).toEqual({
      ok: true,
      value: { phoneHint: null, isConfirmed: false, canChange: true },
    });
    expect(await client.getPhone()).toEqual({
      ok: true,
      value: { phoneHint: '+998*******45', isConfirmed: true, canChange: false },
    });
    expect(await client.createPhoneTelegramLink()).toEqual({ ok: true, value: { telegramLinkUrl: link } });
    expect(received[2]).toMatchObject({ method: 'POST' });
  });

  it.each([
    ['no link', {}],
    ['a link that is no web or Telegram link', { telegramLinkUrl: 'javascript:alert(1)' }],
  ])('refuses a phone link answer with %s, and keeps the own-phone refusal', async (_case, data) => {
    const answers = [
      () => Response.json({ data }),
      () => problem(403, 'Sign in with your phone.', { identityCode: 'main_phone_required' }),
    ];
    const { client } = fakeAccountApi({ 'POST /manage/phone/telegram-link': () => answers.shift()!() });

    const malformed = await client.createPhoneTelegramLink();
    const refused = await client.createPhoneTelegramLink();

    expect(!malformed.ok && malformed.failure.code).toBe('protocol');
    expect(!refused.ok && isIdentityCode(refused.failure, IdentityErrorCodeConstants.mainPhoneRequired)).toBe(true);
  });

  it('lists the sessions with the trusted contact one went through', async () => {
    const own = {
      id: 's1',
      createdAt: '2026-11-03T08:00:00+00:00',
      lastSeenAt: '2026-11-03T09:00:00+00:00',
      expiresAt: '2026-11-10T08:00:00+00:00',
      authenticationMethods: ['otp'],
      ipMasked: '203.0.113.0',
      userAgent: 'Mozilla/5.0',
      isCurrent: true,
    };
    const { client } = fakeAccountApi({
      'GET /manage/sessions': () =>
        Response.json({ data: [own, { ...own, id: 's2', isCurrent: false, trustedContactDisplayName: 'Anvar' }] }),
    });

    const result = await client.getSessions();

    expect(result).toEqual({
      ok: true,
      value: [
        { ...own, trustedContactDisplayName: null },
        { ...own, id: 's2', isCurrent: false, trustedContactDisplayName: 'Anvar' },
      ],
    });
  });

  it('reads the invitation’s trusted contact hint, and none as null', async () => {
    const preview = {
      organizationName: 'Ocharo',
      role: 'owner',
      status: 'pending',
      phoneNumberHint: null,
      emailHint: null,
      expiresAt: '2026-11-09T07:00:00+00:00',
    };
    const answers = [{ ...preview, trustedContactHint: 'A*** K.' }, preview];
    const { client } = fakeAccountApi({ 'GET /invitations/tok-1': () => Response.json({ data: answers.shift() }) });

    const named = await client.getInvitation('tok-1');
    const unnamed = await client.getInvitation('tok-1');

    expect(named.ok && named.value.trustedContactHint).toBe('A*** K.');
    expect(unnamed.ok && unnamed.value.trustedContactHint).toBeNull();
  });
});
