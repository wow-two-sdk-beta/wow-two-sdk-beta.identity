import { flushPromises } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PasswordSignIn, PhoneCodeSignIn } from '../src/kit';
import { fakeAccountApi, noContent, problem } from './FakeAccountApi';
import { button, render } from './Mounting';

describe('password sign-in', () => {
  it('signs in by cookie, naming the client', async () => {
    const { client, received } = fakeAccountApi({ 'POST /sign-in': noContent });
    const wrapper = render(PasswordSignIn, { props: { client, clientId: 'desk' } });

    await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('old-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(received[0]?.body).toEqual({
      identifier: 'ann@example.test',
      password: 'old-password',
      isPersistent: false,
    });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
    expect(wrapper.find('input[type="checkbox"]').exists()).toBe(false);
  });

  it('keeps the person signed in when the instance offers it and they ask', async () => {
    const { client, received } = fakeAccountApi({ 'POST /sign-in': noContent });
    const wrapper = render(PasswordSignIn, { props: { client, isPersistenceOffered: true } });

    expect(wrapper.text()).toContain('Keep me signed in');
    await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('old-password');
    await wrapper.get('input[type="checkbox"]').setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({ identifier: 'ann@example.test', password: 'old-password', isPersistent: true });
  });

  it('hands over the ticket when the password must change first', async () => {
    const required = { isPasswordChangeRequired: true, confirmation: 'None', ticket: 'tk' };
    const { client } = fakeAccountApi({ 'POST /sign-in': () => Response.json({ data: required }) });
    const wrapper = render(PasswordSignIn, { props: { client } });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.emitted('passwordChangeRequired')).toEqual([[required]]);
    expect(wrapper.emitted('signedIn')).toBeUndefined();
  });

  it("shows the problem's detail when the sign-in is refused", async () => {
    const { client } = fakeAccountApi({
      'POST /sign-in': () => problem(401, 'The user name, email or password is incorrect.'),
    });
    const wrapper = render(PasswordSignIn, { props: { client } });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('The user name, email or password is incorrect.');
    expect(wrapper.emitted('signedIn')).toBeUndefined();
  });

  it('puts a field message under its field instead of the summary', async () => {
    const { client } = fakeAccountApi({
      'POST /sign-in': () =>
        problem(400, 'One or more validation errors occurred.', {
          errors: [{ property: 'password', message: 'Enter the password.', code: 'NotEmpty' }],
        }),
    });
    const wrapper = render(PasswordSignIn, { props: { client } });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('Enter the password.');
    expect(wrapper.text()).not.toContain('One or more validation errors occurred.');
  });

  it('offers to resend the confirmation when the email is unconfirmed, carrying the return URL', async () => {
    const { client, received } = fakeAccountApi({
      'POST /sign-in': () =>
        problem(403, 'Confirm your email to sign in.', { identityCode: 'EmailConfirmationRequired' }),
      'POST /resend-confirmation-email': noContent,
    });
    const wrapper = render(PasswordSignIn, {
      props: { client, forgotPasswordHref: '/forgot-password', returnUrl: '/connect/authorize?client_id=desk' },
    });

    await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    await button(wrapper, 'Send the confirmation email again').trigger('click');
    await flushPromises();

    expect(received[1]?.body).toEqual({ email: 'ann@example.test', returnUrl: '/connect/authorize?client_id=desk' });
    expect(wrapper.text()).toContain('We sent the confirmation link to ann@example.test again.');
    expect(wrapper.get('a').attributes('href')).toBe('/forgot-password');
  });

  it.each([
    ['an unconfirmed phone', 'PhoneConfirmationRequired'],
    ['any other refusal', 'SignInNotAllowed'],
  ])('offers no confirmation email for %s', async (_case, identityCode) => {
    const { client } = fakeAccountApi({
      'POST /sign-in': () => problem(403, 'You cannot sign in yet.', { identityCode }),
    });
    const wrapper = render(PasswordSignIn, { props: { client } });

    await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('You cannot sign in yet.');
    expect(wrapper.text()).not.toContain('Send the confirmation email again');
  });

  it('asks for the second factor, then repeats the sign-in with the code', async () => {
    const answers = [
      () => problem(401, 'Enter your two-factor code.', { twoFactorMethod: 'authenticator' }),
      noContent,
    ];
    const { client, received } = fakeAccountApi({ 'POST /sign-in': () => answers.shift()!() });
    const wrapper = render(PasswordSignIn, { props: { client, clientId: 'desk', isPersistenceOffered: true } });

    await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('old-password');
    await wrapper.get('input[type="checkbox"]').setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('Enter the code from your authenticator app.');
    expect(wrapper.text()).not.toContain('Enter your two-factor code.');
    expect(wrapper.emitted('signedIn')).toBeUndefined();
    await wrapper.get('input[name="twoFactorCode"]').setValue(' 123456 ');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(received[1]?.body).toEqual({
      identifier: 'ann@example.test',
      password: 'old-password',
      isPersistent: true,
      twoFactorMethod: 'authenticator',
      twoFactorCode: '123456',
    });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('completes a challenge that carries a ticket at sign-in/two-factor, by a recovery code', async () => {
    const { client, received } = fakeAccountApi({
      'POST /sign-in': () =>
        problem(401, 'Enter your two-factor code.', {
          identityCode: 'TwoFactorRequired',
          twoFactorMethod: 'email',
          userId: 'u1',
          twoFactorTicket: 'tk-2fa',
        }),
      'POST /sign-in/two-factor': () => (received.length > 2 ? noContent() : problem(401, 'The code is invalid.')),
    });
    const wrapper = render(PasswordSignIn, { props: { client, clientId: 'desk', isPersistenceOffered: true } });

    await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('old-password');
    await wrapper.get('input[type="checkbox"]').setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    expect(wrapper.text()).toContain('Enter the code we sent you.');

    await button(wrapper, 'Use a recovery code').trigger('click');
    await wrapper.get('input[name="recoveryCode"]').setValue('wrong-code');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    expect(wrapper.text()).toContain('The code is invalid.');
    expect(wrapper.emitted('signedIn')).toBeUndefined();

    await wrapper.get('input[name="recoveryCode"]').setValue('ABCD-EFGH');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[2]?.url.pathname).toBe('/api/account/sign-in/two-factor');
    expect(received[2]?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(received[2]?.body).toEqual({
      userId: 'u1',
      ticket: 'tk-2fa',
      twoFactorMethod: 'email',
      recoveryCode: 'ABCD-EFGH',
      isPersistent: true,
    });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('goes back to the password from the second factor on request', async () => {
    const { client } = fakeAccountApi({
      'POST /sign-in': () => problem(401, 'Enter your two-factor code.', { identityCode: 'TwoFactorRequired' }),
    });
    const wrapper = render(PasswordSignIn, { props: { client } });

    await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    await button(wrapper, 'Start again').trigger('click');

    expect(wrapper.get<HTMLInputElement>('input[name="identifier"]').element.value).toBe('ann@example.test');
    expect(wrapper.find('input[name="twoFactorCode"]').exists()).toBe(false);
  });
});

describe('phone code sign-in', () => {
  async function sendCode(wrapper: ReturnType<typeof render>) {
    await wrapper.get('input[type="tel"]').setValue('901234567');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
  }

  it('limits the phone to the allowed countries, sends a code and signs in with it', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } }),
      'POST /phone-sign-in': noContent,
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'], clientId: 'desk' } });

    expect(wrapper.text()).toContain('+998');
    expect(wrapper.find('select').exists()).toBe(false);
    await sendCode(wrapper);
    expect(received[0]?.body).toEqual({ phone: '+998901234567' });
    expect(wrapper.text()).toContain('We sent a code to +998901234567.');

    await wrapper.get('input[name="code"]').setValue(' 123456 ');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(received[1]?.body).toEqual({ phone: '+998901234567', code: '123456', isPersistent: false });
    expect(wrapper.find('input[type="checkbox"]').exists()).toBe(false);
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('keeps the person signed in from the code step when the instance offers it', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } }),
      'POST /phone-sign-in': noContent,
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'], isPersistenceOffered: true } });

    await sendCode(wrapper);
    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('input[type="checkbox"]').setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.body).toEqual({ phone: '+998901234567', code: '123456', isPersistent: true });
  });

  it('carries the invitation link that opened it into the sign-in', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } }),
      'POST /phone-sign-in': noContent,
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'], invitationToken: 'tok-1' } });

    await sendCode(wrapper);
    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.body).toEqual({
      phone: '+998901234567',
      code: '123456',
      isPersistent: false,
      invitationToken: 'tok-1',
    });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('opens Telegram first when the phone has no linked chat, then takes the code', async () => {
    const link = 'https://t.me/ocharo_bot?start=abc';
    const { client } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: false, telegramLinkUrl: link } }),
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'] } });

    await sendCode(wrapper);
    const open = wrapper.get(`a[href="${link}"]`);
    expect(open.text()).toBe('Open Telegram');
    expect(open.attributes('rel')).toContain('noopener');
    expect(wrapper.find('input[name="code"]').exists()).toBe(false);

    await open.trigger('click');

    expect(wrapper.find('input[name="code"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('Enter the code the bot sent you in Telegram.');
  });

  it('stays on the phone when no code could be sent', async () => {
    const { client } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: false, telegramLinkUrl: null } }),
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'] } });

    await sendCode(wrapper);

    expect(wrapper.find('input[type="tel"]').exists()).toBe(true);
    expect(wrapper.text()).toContain('We could not send a code. Try again in a minute.');
  });

  it('checks the declared code length before sending, so a typo spends no attempt', async () => {
    const codeRules = [
      { code: 'required', params: null, message: 'Enter the code.' },
      { code: 'length', params: { length: 6, unit: 'characters' }, message: 'The code has 6 characters.' },
    ];
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } }),
      'POST /phone-sign-in': noContent,
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'], codeRules } });

    await sendCode(wrapper);
    await wrapper.get('input[name="code"]').setValue('12345');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.map((request) => request.url.pathname)).toEqual(['/api/account/phone-sign-in/send']);
    expect(wrapper.text()).toContain('The code has 6 characters.');

    await wrapper.get('input[name="code"]').setValue(' 123456 ');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.body).toEqual({ phone: '+998901234567', code: '123456', isPersistent: false });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('says a new code went out, or goes back to Telegram when the bot must send it', async () => {
    const link = 'https://t.me/ocharo_bot?start=abc';
    const answers = [
      { isCodeSent: true, telegramLinkUrl: null },
      { isCodeSent: true, telegramLinkUrl: null },
      { isCodeSent: false, telegramLinkUrl: link },
    ];
    const { client } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: answers.shift() }),
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'] } });

    await sendCode(wrapper);
    await button(wrapper, 'Send a new code').trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('We sent a new code.');

    await button(wrapper, 'Send a new code').trigger('click');
    await flushPromises();

    expect(wrapper.text()).not.toContain('We sent a new code.');
    expect(wrapper.get(`a[href="${link}"]`).text()).toBe('Open Telegram');
    expect(wrapper.find('input[name="code"]').exists()).toBe(false);
  });

  it('asks for the second factor once the code checked out, then signs in with the ticket', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } }),
      'POST /phone-sign-in': () =>
        problem(401, 'Enter your two-factor code.', {
          identityCode: 'TwoFactorRequired',
          twoFactorMethod: 'authenticator',
          userId: 'u1',
          twoFactorTicket: 'tk-2fa',
        }),
      'POST /sign-in/two-factor': noContent,
    });
    const wrapper = render(PhoneCodeSignIn, {
      props: { client, countries: ['UZ'], clientId: 'desk', isPersistenceOffered: true },
    });

    await sendCode(wrapper);
    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('input[type="checkbox"]').setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('Enter the code from your authenticator app.');
    expect(wrapper.emitted('signedIn')).toBeUndefined();
    await wrapper.get('input[name="twoFactorCode"]').setValue('654321');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[2]?.url.pathname).toBe('/api/account/sign-in/two-factor');
    expect(received[2]?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(received[2]?.body).toEqual({
      userId: 'u1',
      ticket: 'tk-2fa',
      twoFactorMethod: 'authenticator',
      twoFactorCode: '654321',
      isPersistent: true,
    });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('goes back to the phone on request', async () => {
    const { client } = fakeAccountApi({
      'POST /phone-sign-in/send': () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } }),
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'] } });

    await sendCode(wrapper);
    await button(wrapper, 'Use another number').trigger('click');

    expect(wrapper.find('input[type="tel"]').exists()).toBe(true);
  });
});

describe('phone code sign-in with trusted contacts', () => {
  const sent = () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } });
  const contacts = { trustedContacts: [{ id: 't1', label: 'L*** K.' }] };
  const choice = {
    token: 'choice-1',
    accounts: [
      { userId: 'u1', displayName: 'Anvar', pictureUrl: null, isSelf: true },
      { userId: 'u2', displayName: 'Lola', pictureUrl: 'https://cdn.example.test/lola.png', isSelf: false },
    ],
  };

  async function enterPhone(wrapper: ReturnType<typeof render>) {
    await wrapper.get('input[type="tel"]').setValue('901234567');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
  }

  /** The destination radios as their labels, and which one is checked. */
  function destinations(wrapper: ReturnType<typeof render>) {
    return wrapper.findAll('input[type="radio"]').map((radio) => ({
      label: radio.element.closest('label')?.textContent?.trim() ?? '',
      checked: (radio.element as HTMLInputElement).checked,
    }));
  }

  it('asks nothing of the contacts while the instance has them off', async () => {
    const { client, received } = fakeAccountApi({ 'POST /phone-sign-in/send': sent });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'] } });

    await enterPhone(wrapper);

    expect(received.map((request) => request.url.pathname)).toEqual(['/api/account/phone-sign-in/send']);
  });

  it('sends the code straight to the phone’s own Telegram when it has no contacts', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/targets': () => Response.json({ data: { trustedContacts: [] } }),
      'POST /phone-sign-in/send': sent,
    });
    const wrapper = render(PhoneCodeSignIn, {
      props: { client, countries: ['UZ'], isTrustedContactSignInEnabled: true },
    });

    await enterPhone(wrapper);

    expect(received.map((request) => [request.url.pathname, request.body])).toEqual([
      ['/api/account/phone-sign-in/targets', { phone: '+998901234567' }],
      ['/api/account/phone-sign-in/send', { phone: '+998901234567' }],
    ]);
    expect(wrapper.find('input[name="code"]').exists()).toBe(true);
  });

  it('offers my Telegram by default, and sends the code to it with no target', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/targets': () => Response.json({ data: contacts }),
      'POST /phone-sign-in/send': sent,
    });
    const wrapper = render(PhoneCodeSignIn, {
      props: { client, countries: ['UZ'], isTrustedContactSignInEnabled: true },
    });

    await enterPhone(wrapper);

    expect(destinations(wrapper)).toEqual([
      { label: expect.stringContaining('My Telegram'), checked: true },
      { label: expect.stringContaining('L*** K.'), checked: false },
    ]);
    expect(wrapper.find('input[name="code"]').exists()).toBe(false);
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.body).toEqual({ phone: '+998901234567' });
    expect(wrapper.text()).toContain('We sent a code to +998901234567.');
  });

  it('sends the code to the contact picked, says so, and sends a new code to the same contact', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/targets': () => Response.json({ data: contacts }),
      'POST /phone-sign-in/send': sent,
      'POST /phone-sign-in': noContent,
    });
    const wrapper = render(PhoneCodeSignIn, {
      props: { client, countries: ['UZ'], isTrustedContactSignInEnabled: true },
    });

    await enterPhone(wrapper);
    await wrapper.findAll('input[type="radio"]')[1]?.setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.body).toEqual({ phone: '+998901234567', targetId: 't1' });
    expect(wrapper.text()).toContain('We sent a code to L*** K. in Telegram. Ask them for it.');
    await button(wrapper, 'Send a new code').trigger('click');
    await flushPromises();
    expect(received[2]?.body).toEqual({ phone: '+998901234567', targetId: 't1' });

    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    expect(received[3]?.body).toEqual({ phone: '+998901234567', code: '123456', isPersistent: false });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('goes back to the phone from the destination, and forgets the contacts', async () => {
    const { client } = fakeAccountApi({ 'POST /phone-sign-in/targets': () => Response.json({ data: contacts }) });
    const wrapper = render(PhoneCodeSignIn, {
      props: { client, countries: ['UZ'], isTrustedContactSignInEnabled: true },
    });

    await enterPhone(wrapper);
    await button(wrapper, 'Use another number').trigger('click');

    expect(wrapper.find('input[type="tel"]').exists()).toBe(true);
    expect(wrapper.find('input[type="radio"]').exists()).toBe(false);
  });

  it('stays on the phone and says why when the contacts could not be read', async () => {
    const { client } = fakeAccountApi({
      'POST /phone-sign-in/targets': () => problem(503, 'Try again in a minute.'),
    });
    const wrapper = render(PhoneCodeSignIn, {
      props: { client, countries: ['UZ'], isTrustedContactSignInEnabled: true },
    });

    await enterPhone(wrapper);

    expect(wrapper.text()).toContain('Try again in a minute.');
    expect(wrapper.find('input[type="tel"]').exists()).toBe(true);
  });

  it('lists the accounts a trusted contact can sign in as, and signs in as the one picked', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': sent,
      'POST /phone-sign-in': () => Response.json({ data: { accountChoice: choice } }),
      'POST /phone-sign-in/choose': noContent,
    });
    const wrapper = render(PhoneCodeSignIn, {
      props: { client, countries: ['UZ'], clientId: 'desk', isPersistenceOffered: true },
    });

    await enterPhone(wrapper);
    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('input[type="checkbox"]').setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.emitted('signedIn')).toBeUndefined();
    const accounts = wrapper.findAll('li');
    expect(accounts).toHaveLength(2);
    expect(accounts[0]?.text()).toContain('Anvar');
    expect(accounts[0]?.text()).toContain('You');
    expect(accounts[1]?.text()).toContain('Lola');
    expect(accounts[1]?.text()).not.toContain('You');
    expect(accounts[1]?.get('img').attributes('src')).toBe('https://cdn.example.test/lola.png');
    await accounts[1]?.get('button').trigger('click');
    await flushPromises();

    expect(received[2]?.url.pathname).toBe('/api/account/phone-sign-in/choose');
    expect(received[2]?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(received[2]?.body).toEqual({ token: 'choice-1', userId: 'u2', isPersistent: true });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('shows why the picked account could not be signed in to, and lets the person pick again', async () => {
    const answers = [() => problem(400, 'This choice has expired.', { identityCode: 'ChoiceExpired' }), noContent];
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': sent,
      'POST /phone-sign-in': () => Response.json({ data: { accountChoice: choice } }),
      'POST /phone-sign-in/choose': () => answers.shift()!(),
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'] } });

    await enterPhone(wrapper);
    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    await wrapper.findAll('li')[0]?.get('button').trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('This choice has expired.');
    expect(wrapper.emitted('signedIn')).toBeUndefined();
    await wrapper.findAll('li')[0]?.get('button').trigger('click');
    await flushPromises();

    expect(received.filter((request) => request.url.pathname.endsWith('/choose'))).toHaveLength(2);
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('goes back to the phone from the account choice', async () => {
    const { client } = fakeAccountApi({
      'POST /phone-sign-in/send': sent,
      'POST /phone-sign-in': () => Response.json({ data: { accountChoice: choice } }),
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'] } });

    await enterPhone(wrapper);
    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    await button(wrapper, 'Use another number').trigger('click');

    expect(wrapper.find('input[type="tel"]').exists()).toBe(true);
    expect(wrapper.find('li').exists()).toBe(false);
  });

  it('asks for the second factor when the picked account has one', async () => {
    const { client, received } = fakeAccountApi({
      'POST /phone-sign-in/send': sent,
      'POST /phone-sign-in': () => Response.json({ data: { accountChoice: choice } }),
      'POST /phone-sign-in/choose': () =>
        problem(401, 'Enter your two-factor code.', {
          identityCode: 'TwoFactorRequired',
          twoFactorMethod: 'authenticator',
          userId: 'u2',
          twoFactorTicket: 'tk-2fa',
        }),
      'POST /sign-in/two-factor': noContent,
    });
    const wrapper = render(PhoneCodeSignIn, { props: { client, countries: ['UZ'] } });

    await enterPhone(wrapper);
    await wrapper.get('input[name="code"]').setValue('123456');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    await wrapper.findAll('li')[1]?.get('button').trigger('click');
    await flushPromises();
    await wrapper.get('input[name="twoFactorCode"]').setValue('654321');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toMatchObject({ userId: 'u2', ticket: 'tk-2fa', twoFactorCode: '654321' });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });
});

describe('phone code sign-in on a new device', () => {
  const now = '2026-11-05T10:00:00Z';
  const sent = () => Response.json({ data: { isCodeSent: true, telegramLinkUrl: null } });
  const pendingAnswer = () => Response.json({ data: { status: 'pending' } });
  const confirmationPath = 'POST /phone-sign-in/confirmation';
  const waiting =
    (expiresAt = '2026-11-05T10:10:00Z') =>
    () =>
      Response.json({ data: { deviceConfirmation: { token: 'dc-1', expiresAt, contacts: ['L*** K.', 'A*** B.'] } } });
  const choice = {
    token: 'choice-1',
    accounts: [
      { userId: 'u1', displayName: 'Anvar', pictureUrl: null, isSelf: true },
      { userId: 'u2', displayName: 'Lola', pictureUrl: null, isSelf: false },
    ],
  };

  // The test utils flush with setImmediate, so only the timers and the clock are faked.
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(new Date(now));
  });
  afterEach(() => vi.useRealTimers());

  const polls = (received: { url: URL; body: unknown }[]) =>
    received.filter((request) => request.url.pathname.endsWith('/confirmation'));

  async function reachWait(answers: Parameters<typeof fakeAccountApi>[0], isPersistenceOffered = false) {
    const api = fakeAccountApi({ 'POST /phone-sign-in/send': sent, ...answers });
    const wrapper = render(PhoneCodeSignIn, {
      props: { client: api.client, countries: ['UZ'], clientId: 'desk', isPersistenceOffered },
    });
    await wrapper.get('input[type="tel"]').setValue('901234567');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    await wrapper.get('input[name="code"]').setValue('123456');
    if (isPersistenceOffered) await wrapper.get('input[type="checkbox"]').setValue(true);
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    return { ...api, wrapper };
  }

  it('waits for a contact’s press after the code, polls every 3 seconds, and signs in on approval', async () => {
    const answers = [pendingAnswer, pendingAnswer, noContent];
    const { wrapper, received } = await reachWait(
      { 'POST /phone-sign-in': waiting(), [confirmationPath]: () => answers.shift()!() },
      true,
    );

    expect(wrapper.text()).toContain(
      'This is a new device. We asked L*** K., A*** B. to confirm it’s you in Telegram. This page continues on its own.',
    );
    expect(wrapper.find('input[name="code"]').exists()).toBe(false);
    expect(polls(received)).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(2999);
    expect(polls(received)).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(1);
    expect(polls(received)).toHaveLength(1);
    expect(polls(received)[0]?.url.search).toBe('?useCookies=true&clientId=desk');
    expect(polls(received)[0]?.body).toEqual({ token: 'dc-1', isPersistent: true });
    await vi.advanceTimersByTimeAsync(3000);
    expect(polls(received)).toHaveLength(2);
    expect(wrapper.emitted('signedIn')).toBeUndefined();
    await vi.advanceTimersByTimeAsync(3000);

    expect(polls(received)).toHaveLength(3);
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
    await vi.advanceTimersByTimeAsync(30000);
    expect(polls(received)).toHaveLength(3);
  });

  it('keeps one request in flight at most, however long the answer takes', async () => {
    let release: (answer: Response) => void = () => undefined;
    const slow = new Promise<Response>((resolve) => (release = resolve));
    const { received, fetch, wrapper } = await reachWait({
      'POST /phone-sign-in': waiting(),
      [confirmationPath]: pendingAnswer,
    });
    const original = fetch.getMockImplementation()!;
    fetch.mockImplementation(async (input, init) => {
      const url = new URL(String(input));
      if (url.pathname.endsWith('/confirmation')) {
        received.push({ method: 'POST', url, body: undefined });
        return slow;
      }
      return original(input, init);
    });

    await vi.advanceTimersByTimeAsync(30000);
    expect(polls(received)).toHaveLength(1);
    release(noContent());
    await flushPromises();

    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('keeps polling after a poll that never reached the service', async () => {
    const answers = [
      () => {
        throw new TypeError('Failed to fetch');
      },
      () => problem(503, 'Try again in a minute.'),
      noContent,
    ];
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': waiting(),
      [confirmationPath]: () => answers.shift()!(),
    });

    await vi.advanceTimersByTimeAsync(3000);
    expect(wrapper.text()).not.toContain('Start again');
    await vi.advanceTimersByTimeAsync(3000);
    expect(wrapper.text()).not.toContain('Try again in a minute.');
    await vi.advanceTimersByTimeAsync(3000);

    expect(polls(received)).toHaveLength(3);
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('shows a denial, stops polling, and starts again with a new code', async () => {
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': waiting(),
      [confirmationPath]: () =>
        problem(403, 'A contact denied this sign-in.', { identityCode: 'DeviceConfirmationDenied' }),
    });

    await vi.advanceTimersByTimeAsync(3000);

    expect(wrapper.text()).toContain('A contact denied this sign-in.');
    expect(wrapper.emitted('signedIn')).toBeUndefined();
    await vi.advanceTimersByTimeAsync(30000);
    expect(polls(received)).toHaveLength(1);
    await button(wrapper, 'Start again').trigger('click');
    await flushPromises();

    expect(received.filter((request) => request.url.pathname.endsWith('/send'))).toHaveLength(2);
    expect(wrapper.find('input[name="code"]').exists()).toBe(true);
    expect(wrapper.text()).not.toContain('A contact denied this sign-in.');
  });

  it('offers another number after a denial', async () => {
    const { wrapper } = await reachWait({
      'POST /phone-sign-in': waiting(),
      [confirmationPath]: () => problem(403, 'Denied.', { identityCode: 'DeviceConfirmationDenied' }),
    });

    await vi.advanceTimersByTimeAsync(3000);
    await button(wrapper, 'Use another number').trigger('click');

    expect(wrapper.find('input[type="tel"]').exists()).toBe(true);
  });

  it('shows that the request ended when the service says so', async () => {
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': waiting(),
      [confirmationPath]: () => problem(401, 'This request has ended.', { identityCode: 'DeviceConfirmationExpired' }),
    });

    await vi.advanceTimersByTimeAsync(3000);

    expect(wrapper.text()).toContain('This request has ended.');
    expect(button(wrapper, 'Start again').exists()).toBe(true);
    expect(button(wrapper, 'Use another number').exists()).toBe(true);
    await vi.advanceTimersByTimeAsync(30000);
    expect(polls(received)).toHaveLength(1);
  });

  it('keeps waiting on a browser whose clock runs ahead, and signs in on approval', async () => {
    vi.setSystemTime(new Date('2026-11-05T11:00:00Z'));
    const answers = [pendingAnswer, noContent];
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': waiting('2026-11-05T10:10:00Z'),
      [confirmationPath]: () => answers.shift()!(),
    });

    await vi.advanceTimersByTimeAsync(3000);
    expect(polls(received)).toHaveLength(1);
    expect(wrapper.text()).not.toContain('Start again');
    await vi.advanceTimersByTimeAsync(3000);

    expect(polls(received)).toHaveLength(2);
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('leaves the end of the request to the service, past `expiresAt` on the browser’s clock', async () => {
    let isEnded = false;
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': waiting('2026-11-05T10:00:10Z'),
      [confirmationPath]: () =>
        isEnded ? problem(401, 'This request has ended.', { identityCode: 'DeviceConfirmationExpired' }) : pendingAnswer(),
    });

    await vi.advanceTimersByTimeAsync(30000);
    expect(polls(received)).toHaveLength(10);
    expect(wrapper.text()).not.toContain('Start again');
    isEnded = true;
    await vi.advanceTimersByTimeAsync(3000);

    expect(wrapper.text()).toContain('This request has ended.');
    expect(button(wrapper, 'Start again').exists()).toBe(true);
    await vi.advanceTimersByTimeAsync(30000);
    expect(polls(received)).toHaveLength(11);
  });

  it('goes to the second factor when the confirmed account has one', async () => {
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': waiting(),
      [confirmationPath]: () =>
        problem(401, 'Enter your two-factor code.', {
          identityCode: 'TwoFactorRequired',
          twoFactorMethod: 'authenticator',
          userId: 'u1',
          twoFactorTicket: 'tk-2fa',
        }),
      'POST /sign-in/two-factor': noContent,
    });

    await vi.advanceTimersByTimeAsync(3000);
    await wrapper.get('input[name="twoFactorCode"]').setValue('654321');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received.at(-1)?.body).toMatchObject({ userId: 'u1', ticket: 'tk-2fa', twoFactorCode: '654321' });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
    await vi.advanceTimersByTimeAsync(30000);
    expect(polls(received)).toHaveLength(1);
  });

  it('stops polling when the person leaves for another number', async () => {
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': waiting(),
      [confirmationPath]: pendingAnswer,
    });

    await vi.advanceTimersByTimeAsync(3000);
    expect(polls(received)).toHaveLength(1);
    await button(wrapper, 'Use another number').trigger('click');
    await vi.advanceTimersByTimeAsync(30000);

    expect(polls(received)).toHaveLength(1);
    expect(wrapper.find('input[type="tel"]').exists()).toBe(true);
  });

  it('stops polling when the page goes away', async () => {
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': waiting(),
      [confirmationPath]: pendingAnswer,
    });

    await vi.advanceTimersByTimeAsync(3000);
    wrapper.unmount();
    await vi.advanceTimersByTimeAsync(30000);

    expect(polls(received)).toHaveLength(1);
  });

  it('waits after the account choice, keeping the choice’s persistence', async () => {
    const { wrapper, received } = await reachWait(
      {
        'POST /phone-sign-in': () => Response.json({ data: { accountChoice: choice } }),
        'POST /phone-sign-in/choose': waiting(),
        [confirmationPath]: noContent,
      },
      true,
    );

    await wrapper.findAll('li')[1]?.get('button').trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('We asked L*** K., A*** B. to confirm it’s you in Telegram.');
    expect(wrapper.emitted('signedIn')).toBeUndefined();
    await vi.advanceTimersByTimeAsync(3000);

    expect(polls(received)[0]?.body).toEqual({ token: 'dc-1', isPersistent: true });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
  });

  it('shows that no contact could be asked, like any other failure', async () => {
    const { wrapper, received } = await reachWait({
      'POST /phone-sign-in': () =>
        problem(409, 'Nobody could be asked. Try again.', { identityCode: 'DeviceConfirmationUnavailable' }),
    });

    expect(wrapper.text()).toContain('Nobody could be asked. Try again.');
    expect(wrapper.find('input[name="code"]').exists()).toBe(true);
    await vi.advanceTimersByTimeAsync(30000);
    expect(polls(received)).toHaveLength(0);
  });
});
