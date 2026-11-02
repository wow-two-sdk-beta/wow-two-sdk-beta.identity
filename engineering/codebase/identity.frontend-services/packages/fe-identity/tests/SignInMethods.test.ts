import { flushPromises } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
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
