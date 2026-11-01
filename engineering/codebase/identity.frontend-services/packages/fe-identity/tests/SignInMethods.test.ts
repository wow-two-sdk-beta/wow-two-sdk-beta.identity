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
    expect(received[0]?.body).toEqual({ identifier: 'ann@example.test', password: 'old-password' });
    expect(wrapper.emitted('signedIn')).toEqual([[]]);
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

  it('offers to resend the confirmation when the email is unconfirmed', async () => {
    const { client, received } = fakeAccountApi({
      'POST /sign-in': () => problem(403, 'Confirm your email or phone to sign in.'),
      'POST /resend-confirmation-email': noContent,
    });
    const wrapper = render(PasswordSignIn, { props: { client, forgotPasswordHref: '/forgot-password' } });

    await wrapper.get('input[name="identifier"]').setValue('ann@example.test');
    await wrapper.get('form').trigger('submit');
    await flushPromises();
    await button(wrapper, 'Send the confirmation email again').trigger('click');
    await flushPromises();

    expect(received[1]?.body).toEqual({ email: 'ann@example.test' });
    expect(wrapper.text()).toContain('We sent the confirmation link to ann@example.test again.');
    expect(wrapper.get('a').attributes('href')).toBe('/forgot-password');
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
    expect(received[1]?.body).toEqual({ phone: '+998901234567', code: '123456' });
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
