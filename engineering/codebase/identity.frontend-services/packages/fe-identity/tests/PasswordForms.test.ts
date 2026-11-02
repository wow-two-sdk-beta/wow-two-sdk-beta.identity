import { flushPromises } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import type { SignInRuleDto } from '../src/contracts';
import { ChangePasswordForm, ForgotPasswordForm, ResetPasswordForm, SignUpForm } from '../src/kit';
import { fakeAccountApi, noContent, problem } from './FakeAccountApi';
import { button, render } from './Mounting';

/** The rules an instance declares for a new password, the messages in Russian as a `ru` request gets them. */
const passwordRules: ReadonlyArray<SignInRuleDto> = [
  { code: 'min', params: { min: 15, unit: 'characters' }, message: 'Пароль должен содержать не менее 15 символов.' },
  { code: 'pattern', params: { pattern: '[0-9]' }, message: 'Пароль должен содержать цифру.' },
  {
    code: 'notEqual',
    params: { fields: ['userName', 'email'], isCaseIgnored: true },
    message: 'Пароль должен отличаться от имени и почты.',
  },
];

/** The message shown under the input named `name`, found through the input's `aria-describedby`. */
function errorUnder(wrapper: ReturnType<typeof render>, name: string) {
  const id = wrapper.get(`input[name="${name}"]`).attributes('aria-describedby');
  return id ? wrapper.find(`[id="${id}"]`).text() : null;
}

/** The requests that changed something, so a blocked submit shows as none. */
const writes = <T extends { method: string }>(received: ReadonlyArray<T>) =>
  received.filter(({ method }) => method !== 'GET');

describe('sign-up form', () => {
  it('creates the account and asks the person to confirm the email', async () => {
    const { client, received } = fakeAccountApi({
      'POST /sign-up': () => Response.json({ data: { userId: 'u1', isConfirmationSent: true } }),
    });
    const wrapper = render(SignUpForm, { props: { client, signInHref: '/sign-in' } });

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('a-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({ email: 'ann@example.test', password: 'a-long-password' });
    expect(wrapper.text()).toContain('we sent a link to ann@example.test');
    expect(wrapper.get('a').attributes('href')).toBe('/sign-in');
    expect(wrapper.emitted('signedUp')).toEqual([[{ userId: 'u1', isConfirmationSent: true }]]);
  });

  it('signs up with the invitation link that opened it', async () => {
    const { client, received } = fakeAccountApi({ 'POST /sign-up': () => Response.json({ data: { userId: 'u2' } }) });
    const wrapper = render(SignUpForm, { props: { client, invitationToken: 'tok-1' } });

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('a-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({
      email: 'ann@example.test',
      password: 'a-long-password',
      invitationToken: 'tok-1',
    });
  });

  it('says so when the instance keeps sign-up closed', async () => {
    const { client } = fakeAccountApi({
      'POST /sign-up': () => problem(403, 'Sign-up is by invitation only; ask an admin for a link.'),
    });
    const wrapper = render(SignUpForm, { props: { client } });

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('a-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('Sign-up is by invitation only; ask an admin for a link.');
    expect(wrapper.find('form').exists()).toBe(true);
  });

  it('shows an email that is not one under its field as the person leaves it, and sends nothing', async () => {
    const { client, received } = fakeAccountApi({});
    const wrapper = render(SignUpForm, { props: { client } });

    await wrapper.get('input[name="email"]').setValue('sdfsdf');
    await wrapper.get('input[name="email"]').trigger('blur');
    await flushPromises();
    expect(errorUnder(wrapper, 'email')).toBe('must be a valid email address');
    expect(wrapper.get('input[name="email"]').attributes('aria-invalid')).toBe('true');

    await wrapper.get('input[name="password"]').setValue('a-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'email')).toBe('must be a valid email address');
    expect(received).toHaveLength(0);
    expect(wrapper.emitted('signedUp')).toBeUndefined();
  });

  it('requires an email and a password even with no rules declared', async () => {
    const { client, received } = fakeAccountApi({});
    const wrapper = render(SignUpForm, { props: { client } });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'email')).toBe('is required');
    expect(errorUnder(wrapper, 'password')).toBe('is required');
    expect(received).toHaveLength(0);
  });

  it('checks only presence of the password while no rules are passed', async () => {
    const { client, received } = fakeAccountApi({
      'POST /sign-up': () => Response.json({ data: { userId: 'u1', isConfirmationSent: false } }),
    });
    const wrapper = render(SignUpForm, { props: { client } });

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('x');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({ email: 'ann@example.test', password: 'x' });
  });

  it('blocks a password that breaks the declared rules and shows their messages under the field', async () => {
    const { client, received } = fakeAccountApi({});
    const wrapper = render(SignUpForm, { props: { client, passwordRules } });

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'password')).toBe(
      'Пароль должен содержать не менее 15 символов.Пароль должен содержать цифру.',
    );
    expect(errorUnder(wrapper, 'email')).toBeNull();
    expect(received).toHaveLength(0);
  });

  it('blocks a password equal to the email, ignoring case', async () => {
    const { client, received } = fakeAccountApi({});
    const wrapper = render(SignUpForm, { props: { client, passwordRules } });

    await wrapper.get('input[name="email"]').setValue('ann.long.name1@example.test');
    await wrapper.get('input[name="password"]').setValue('ANN.long.name1@example.test');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'password')).toBe('Пароль должен отличаться от имени и почты.');
    expect(received).toHaveLength(0);
  });

  it('sends the account once the declared rules hold', async () => {
    const { client, received } = fakeAccountApi({
      'POST /sign-up': () => Response.json({ data: { userId: 'u1', isConfirmationSent: true } }),
    });
    const wrapper = render(SignUpForm, { props: { client, passwordRules } });

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('a-long-password-7');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({ email: 'ann@example.test', password: 'a-long-password-7' });
  });

  it('applies rules that arrive after the form shows', async () => {
    const { client, received } = fakeAccountApi({});
    const wrapper = render(SignUpForm, { props: { client } });
    await wrapper.setProps({ passwordRules } as Record<string, unknown>);

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('input[name="password"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'password')).toContain('Пароль должен содержать не менее 15 символов.');
    expect(received).toHaveLength(0);
  });
});

describe('forgot password form', () => {
  it('sends the reset link and reveals nothing about the account', async () => {
    const { client, received } = fakeAccountApi({ 'POST /forgot-password': noContent });
    const wrapper = render(ForgotPasswordForm, { props: { client } });

    await wrapper.get('input[name="email"]').setValue('ann@example.test');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({ email: 'ann@example.test' });
    expect(wrapper.text()).toContain('If an account uses ann@example.test, we sent it a link');
  });
});

describe('reset password form', () => {
  it('sets the new password with the emailed token', async () => {
    const { client, received } = fakeAccountApi({ 'POST /reset-password': noContent });
    const wrapper = render(ResetPasswordForm, { props: { client, email: 'ann@example.test', token: 't0k' } });

    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({
      email: 'ann@example.test',
      resetToken: 't0k',
      newPassword: 'a-new-long-password',
    });
    expect(wrapper.text()).toContain('Your password is changed.');
  });

  it('shows why an expired link was refused', async () => {
    const { client } = fakeAccountApi({
      'POST /reset-password': () =>
        problem(400, 'One or more validation errors occurred.', {
          errors: [{ property: '', message: 'The reset link is invalid or has expired.', code: 'InvalidToken' }],
        }),
    });
    const wrapper = render(ResetPasswordForm, { props: { client, email: 'ann@example.test', token: 'old' } });

    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('The reset link is invalid or has expired.');
  });

  it('requires the new password', async () => {
    const { client, received } = fakeAccountApi({ 'POST /reset-password': noContent });
    const wrapper = render(ResetPasswordForm, { props: { client, email: 'ann@example.test', token: 't0k' } });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toBe('is required');
    expect(received).toHaveLength(0);
  });

  it('blocks a new password that breaks the declared rules', async () => {
    const { client, received } = fakeAccountApi({ 'POST /reset-password': noContent });
    const wrapper = render(ResetPasswordForm, {
      props: { client, email: 'ann@example.test', token: 't0k', passwordRules },
    });

    await wrapper.get('input[name="newPassword"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toBe(
      'Пароль должен содержать не менее 15 символов.Пароль должен содержать цифру.',
    );
    expect(received).toHaveLength(0);
  });

  it('blocks a new password equal to the email the link names', async () => {
    const { client, received } = fakeAccountApi({ 'POST /reset-password': noContent });
    const wrapper = render(ResetPasswordForm, {
      props: { client, email: 'ann.long.name1@example.test', token: 't0k', passwordRules },
    });

    await wrapper.get('input[name="newPassword"]').setValue('Ann.Long.Name1@example.test');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toBe('Пароль должен отличаться от имени и почты.');
    expect(received).toHaveLength(0);
  });

  it('sets a new password that meets the declared rules', async () => {
    const { client, received } = fakeAccountApi({ 'POST /reset-password': noContent });
    const wrapper = render(ResetPasswordForm, {
      props: { client, email: 'ann@example.test', token: 't0k', passwordRules },
    });

    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password-7');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({
      email: 'ann@example.test',
      resetToken: 't0k',
      newPassword: 'a-new-long-password-7',
    });
  });
});

describe('change password form', () => {
  const confirmation =
    (type: string, isSent = true) =>
    () =>
      Response.json({ data: { confirmation: type, isSent } });

  it('opens the form at once under None and changes the password', async () => {
    const { client, received } = fakeAccountApi({
      'POST /manage/password/confirmation': confirmation('None', false),
      'POST /manage/password': noContent,
    });
    const wrapper = render(ChangePasswordForm, { props: { client } });

    expect(received).toHaveLength(0);
    await button(wrapper, 'Change the password').trigger('click');
    await flushPromises();
    expect(wrapper.find('input[name="code"]').exists()).toBe(false);

    await wrapper.get('input[name="currentPassword"]').setValue('old-password');
    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.body).toEqual({ currentPassword: 'old-password', newPassword: 'a-new-long-password' });
    expect(wrapper.text()).toContain('Your password is changed.');
    expect(wrapper.emitted('changed')).toHaveLength(1);
  });

  it('asks for the sent code under Code, starting a required change at once with its ticket', async () => {
    const { client, received } = fakeAccountApi({
      'POST /manage/password/confirmation': confirmation('Code'),
      'POST /manage/password': noContent,
    });
    const wrapper = render(ChangePasswordForm, { props: { client, ticket: 'tk', confirmation: 'Code' } });
    await flushPromises();

    expect(received[0]?.body).toEqual({ ticket: 'tk' });
    expect(wrapper.text()).toContain('Your password no longer meets the rules here.');
    expect(wrapper.text()).toContain('We sent you a code.');
    expect(wrapper.find('input[name="currentPassword"]').exists()).toBe(false);
    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password');
    await wrapper.get('input[name="code"]').setValue('482913');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[1]?.body).toEqual({ ticket: 'tk', newPassword: 'a-new-long-password', code: '482913' });
  });

  it('opens a required change under None without asking the instance first', async () => {
    const { client, received } = fakeAccountApi({ 'POST /manage/password': noContent });
    const wrapper = render(ChangePasswordForm, { props: { client, ticket: 'tk', confirmation: 'None' } });
    await flushPromises();

    expect(received).toHaveLength(0);
    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({ ticket: 'tk', newPassword: 'a-new-long-password' });
    expect(wrapper.emitted('changed')).toHaveLength(1);
  });

  it('shows no form under Link, only that the link went out', async () => {
    const { client } = fakeAccountApi({ 'POST /manage/password/confirmation': confirmation('Link') });
    const wrapper = render(ChangePasswordForm, { props: { client } });

    await button(wrapper, 'Change the password').trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('We sent you a link to change the password.');
    expect(wrapper.find('input').exists()).toBe(false);
  });

  it('spends a change link token with only the new password', async () => {
    const { client, received } = fakeAccountApi({ 'POST /password/change': noContent });
    const wrapper = render(ChangePasswordForm, { props: { client, token: 'link-token' } });

    expect(wrapper.find('input[name="currentPassword"]').exists()).toBe(false);
    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({ token: 'link-token', newPassword: 'a-new-long-password' });
    expect(wrapper.emitted('changed')).toHaveLength(1);
  });

  it('says why the change could not start', async () => {
    const { client } = fakeAccountApi({
      'POST /manage/password/confirmation': () => problem(401, 'Sign in to change the password.'),
    });
    const wrapper = render(ChangePasswordForm, { props: { client } });

    await button(wrapper, 'Change the password').trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('Sign in to change the password.');
  });

  it('requires the current password and the new one', async () => {
    const { client, received } = fakeAccountApi({
      'POST /manage/password/confirmation': confirmation('None', false),
      'POST /manage/password': noContent,
    });
    const wrapper = render(ChangePasswordForm, { props: { client } });
    await button(wrapper, 'Change the password').trigger('click');
    await flushPromises();

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'currentPassword')).toBe('is required');
    expect(errorUnder(wrapper, 'newPassword')).toBe('is required');
    expect(writes(received).map((request) => request.url.pathname)).toEqual([
      '/api/account/manage/password/confirmation',
    ]);
  });

  it('requires the sent code under Code', async () => {
    const { client, received } = fakeAccountApi({
      'POST /manage/password/confirmation': confirmation('Code'),
      'POST /manage/password': noContent,
    });
    const wrapper = render(ChangePasswordForm, { props: { client, ticket: 'tk', confirmation: 'Code' } });
    await flushPromises();

    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'code')).toBe('is required');
    expect(errorUnder(wrapper, 'newPassword')).toBeNull();
    expect(received.map((request) => request.url.pathname)).not.toContain('/api/account/manage/password');
  });

  it('blocks a new password that breaks the declared rules, for every way into the form', async () => {
    const { client, received } = fakeAccountApi({ 'POST /password/change': noContent });
    const wrapper = render(ChangePasswordForm, { props: { client, token: 'link-token', passwordRules } });

    await wrapper.get('input[name="newPassword"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toBe(
      'Пароль должен содержать не менее 15 символов.Пароль должен содержать цифру.',
    );
    expect(received).toHaveLength(0);

    await wrapper.get('input[name="newPassword"]').setValue('a-new-long-password-7');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(received[0]?.body).toEqual({ token: 'link-token', newPassword: 'a-new-long-password-7' });
    expect(wrapper.emitted('changed')).toHaveLength(1);
  });

  it('checks the new password of a required change under its ticket', async () => {
    const { client, received } = fakeAccountApi({ 'POST /manage/password': noContent });
    const wrapper = render(ChangePasswordForm, {
      props: { client, ticket: 'tk', confirmation: 'None', passwordRules },
    });
    await flushPromises();

    await wrapper.get('input[name="newPassword"]').setValue('short');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(errorUnder(wrapper, 'newPassword')).toContain('Пароль должен содержать не менее 15 символов.');
    expect(received).toHaveLength(0);
  });
});
