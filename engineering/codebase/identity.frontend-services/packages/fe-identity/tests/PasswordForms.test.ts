import { flushPromises } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { ChangePasswordForm, ForgotPasswordForm, ResetPasswordForm, SignUpForm } from '../src/kit';
import { fakeAccountApi, noContent, problem } from './FakeAccountApi';
import { button, render } from './Mounting';

describe('sign-up form', () => {
  it('creates the account and asks the person to confirm the email', async () => {
    const { client, received } = fakeAccountApi({
      'POST /register': () => Response.json({ data: { userId: 'u1', isConfirmationSent: true } }),
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

  it('says so when the instance keeps sign-up closed', async () => {
    const { client } = fakeAccountApi({
      'POST /register': () => problem(403, 'Sign-up is closed; ask an admin to add you.'),
    });
    const wrapper = render(SignUpForm, { props: { client } });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('Sign-up is closed; ask an admin to add you.');
    expect(wrapper.find('form').exists()).toBe(true);
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

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.text()).toContain('The reset link is invalid or has expired.');
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
});
