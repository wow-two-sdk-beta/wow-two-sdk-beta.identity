import { flushPromises, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { SignUpProfileDto } from '../src/contracts';
import { InviteSignUpForm } from '../src/kit';
import { fakeAccountApi, problem } from './FakeAccountApi';
import { button, render } from './Mounting';

const link = 'https://t.me/ocharo_bot?start=su-1';
const rules = {
  acceptedTypes: ['image/jpeg', 'image/png', 'image/webp'],
  maxBytes: 5_242_880,
  minDimension: 256,
  maxDimension: 4096,
};

function profileOf(displayName: string, picture: string, pictureRules = rules) {
  return { displayName, picture, pictureRules } as SignUpProfileDto;
}

const picture = (type = 'image/png', size = 64) => new File([new Uint8Array(size)], 'me.png', { type });
const status = (data: Record<string, unknown>) => () =>
  Response.json({ data: { failureCode: null, isSignedIn: false, ...data } });

/** Decodes every picture at `width` × `height`, where the browser would. */
function decodeAt(width: number, height: number) {
  vi.stubGlobal(
    'createImageBitmap',
    vi.fn(async () => ({ width, height, close: () => undefined })),
  );
}

/** Picks `file` in the picture field, as the browser's file dialog does, and lets its checks finish. */
async function pick(wrapper: VueWrapper, file: File) {
  const input = wrapper.get('input[type="file"]');
  Object.defineProperty(input.element, 'files', { value: [file], configurable: true });
  await input.trigger('change');
  await flushPromises();
}

/** The error shown under the input named `name`, found through the input's `aria-describedby`; not its helper. */
function errorUnder(wrapper: VueWrapper, name: string) {
  const ids = wrapper.get(`input[name="${name}"]`).attributes('aria-describedby')?.split(' ') ?? [];
  const id = ids.find((candidate) => candidate.endsWith('-error'));
  return id ? wrapper.get(`[id="${id}"]`).text() : null;
}

async function submit(wrapper: VueWrapper) {
  await wrapper.get('form').trigger('submit');
  await flushPromises();
}

beforeEach(() => {
  vi.spyOn(window, 'open').mockReturnValue(null);
  vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:preview');
  vi.spyOn(URL, 'revokeObjectURL').mockReturnValue(undefined);
  decodeAt(512, 512);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('invitation sign-up form fields', () => {
  it('asks for nothing but the phone check while the profile is off, and opens the bot', async () => {
    const { client, received } = fakeAccountApi({
      'POST /invitations/tok-1/sign-up': () => Response.json({ data: { telegramLinkUrl: link, expiresAt: '' } }),
    });
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('off', 'off'), clientId: 'desk' },
    });

    expect(wrapper.find('input').exists()).toBe(false);
    await button(wrapper, 'Verify phone in Telegram').trigger('click');
    await flushPromises();

    expect(received[0]?.url.search).toBe('?clientId=desk');
    expect([...(received[0]?.body as FormData).keys()]).toEqual([]);
    expect(window.open).toHaveBeenCalledWith(link, '_blank', 'noopener,noreferrer');
    expect(
      wrapper
        .findAll('a')
        .find((anchor) => anchor.text() === 'Open Telegram')
        ?.attributes('href'),
    ).toBe(link);
    expect(wrapper.text()).toContain('Waiting for your contact');
  });

  it('requires the name and picture the profile requires, sending nothing without them', async () => {
    const { client, received } = fakeAccountApi({});
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('required', 'required') },
    });

    await submit(wrapper);

    expect(errorUnder(wrapper, 'displayName')).toBe('Enter your name.');
    expect(errorUnder(wrapper, 'picture')).toBe('Choose a picture.');
    expect(received).toHaveLength(0);
  });

  it('leaves optional fields out of the request when left empty, and trims the name', async () => {
    const { client, received } = fakeAccountApi({
      'POST /invitations/tok-1/sign-up': () => Response.json({ data: { telegramLinkUrl: link, expiresAt: '' } }),
    });
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('optional', 'optional') },
    });

    await submit(wrapper);
    await button(wrapper, 'Start over').trigger('click');
    await wrapper.get('input[name="displayName"]').setValue('  Lola  ');
    await submit(wrapper);

    expect([...(received[0]?.body as FormData).keys()]).toEqual([]);
    expect([...(received[1]?.body as FormData).entries()]).toEqual([['displayName', 'Lola']]);
  });

  it('refuses a name longer than the service keeps', async () => {
    const { client, received } = fakeAccountApi({});
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('required', 'off') },
    });

    await wrapper.get('input[name="displayName"]').setValue('L'.repeat(121));
    await submit(wrapper);

    expect(errorUnder(wrapper, 'displayName')).toBe('Use at most 120 characters.');
    expect(received).toHaveLength(0);
  });

  it('accepts exactly the declared picture types, previews a fitting picture round and sends it', async () => {
    const { client, received } = fakeAccountApi({
      'POST /invitations/tok-1/sign-up': () => Response.json({ data: { telegramLinkUrl: link, expiresAt: '' } }),
    });
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('off', 'required') },
    });

    expect(wrapper.get('input[type="file"]').attributes('accept')).toBe('image/jpeg,image/png,image/webp');
    expect(wrapper.text()).toContain('JPEG, PNG or WebP, up to 5 MB, at least 256 pixels on each side.');
    const chosen = picture();
    await pick(wrapper, chosen);

    expect(wrapper.get('img').attributes('src')).toBe('blob:preview');
    expect(wrapper.find('.rounded-full').exists()).toBe(true);
    expect(button(wrapper, 'Choose another picture').exists()).toBe(true);
    await submit(wrapper);
    const sent = (received[0]?.body as FormData).get('picture') as File;
    expect([sent.name, sent.type, sent.size]).toEqual([chosen.name, chosen.type, chosen.size]);
  });

  it.each([
    ['a type the rules do not list', picture('image/gif'), [512, 512], 'Choose a JPEG, PNG or WebP picture.'],
    ['more bytes than the rules allow', picture('image/png', 2048), [512, 512], 'Choose a picture of at most 1 KB.'],
    ['a side under the minimum', picture(), [512, 200], 'Choose a picture at least 256 pixels on each side.'],
    ['a side over the maximum', picture(), [5000, 512], 'Choose a picture at most 4096 pixels on each side.'],
  ])('refuses a picture with %s before sending it', async (_case, file, [width, height], message) => {
    decodeAt(width!, height!);
    const { client, received } = fakeAccountApi({});
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('off', 'required', { ...rules, maxBytes: 1024 }) },
    });

    await pick(wrapper, file);

    expect(errorUnder(wrapper, 'picture')).toBe(message);
    expect(wrapper.find('img').exists()).toBe(false);
    await submit(wrapper);
    expect(received).toHaveLength(0);
  });

  it('refuses a picture the browser cannot decode, and checks no sides where it decodes none', async () => {
    vi.stubGlobal(
      'createImageBitmap',
      vi.fn(async () => Promise.reject(new Error('broken'))),
    );
    const { client } = fakeAccountApi({});
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('off', 'optional') },
    });

    await pick(wrapper, picture());
    expect(errorUnder(wrapper, 'picture')).toBe('We could not read this picture. Choose another one.');

    vi.stubGlobal('createImageBitmap', undefined);
    await pick(wrapper, picture());
    expect(errorUnder(wrapper, 'picture')).toBeNull();
    expect(wrapper.get('img').attributes('src')).toBe('blob:preview');
  });

  it("puts the service's refusal of a field under that field", async () => {
    const answers = [
      () => problem(400, 'The picture is too small.', { identityCode: 'PictureTooSmall' }),
      () => problem(400, 'The name is too long.', { identityCode: 'DisplayNameTooLong' }),
    ];
    const { client } = fakeAccountApi({ 'POST /invitations/tok-1/sign-up': () => answers.shift()!() });
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('required', 'required') },
    });

    await wrapper.get('input[name="displayName"]').setValue('Lola');
    await pick(wrapper, picture());
    await submit(wrapper);
    expect(errorUnder(wrapper, 'picture')).toBe('Choose a picture at least 256 pixels on each side.');

    await submit(wrapper);
    expect(errorUnder(wrapper, 'displayName')).toBe('Use at most 120 characters.');
    expect(wrapper.text()).not.toContain('The name is too long.');
  });

  it("shows the service's reason for any other refusal", async () => {
    const { client } = fakeAccountApi({
      'POST /invitations/tok-1/sign-up': () =>
        problem(409, 'This invitation has expired.', { identityCode: 'InvitationExpired' }),
    });
    const wrapper = render(InviteSignUpForm, {
      props: { client, invitationToken: 'tok-1', profile: profileOf('off', 'off') },
    });

    await submit(wrapper);

    expect(wrapper.text()).toContain('This invitation has expired.');
    expect(window.open).not.toHaveBeenCalled();
  });
});

describe('invitation sign-up polling', () => {
  /** Mounts a form with no profile, starts the attempt and leaves the page waiting for the bot. */
  async function waiting(statuses: Array<() => Response>, expiresInMs = 600_000) {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    const expiresAt = new Date(Date.now() + expiresInMs).toISOString();
    const api = fakeAccountApi({
      'POST /invitations/tok-1/sign-up': () => Response.json({ data: { telegramLinkUrl: link, expiresAt } }),
      'GET /invitations/sign-up/status': () => (statuses.length > 1 ? statuses.shift()! : statuses[0]!)(),
    });
    const wrapper = render(InviteSignUpForm, {
      props: { client: api.client, invitationToken: 'tok-1', profile: profileOf('off', 'off'), clientId: 'desk' },
    });
    await submit(wrapper);
    const polls = () => api.received.filter((request) => request.url.pathname.endsWith('/status'));
    return { wrapper, polls };
  }

  it('polls every 2 s until the person joined, then signs them up and stops', async () => {
    const { wrapper, polls } = await waiting([
      status({ status: 'pending' }),
      status({ status: 'pending' }),
      status({ status: 'completed', isSignedIn: true }),
    ]);

    await vi.advanceTimersByTimeAsync(1999);
    expect(polls()).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(1);
    expect(polls()).toHaveLength(1);
    expect(polls()[0]?.url.search).toBe('?clientId=desk');
    await vi.advanceTimersByTimeAsync(4000);
    expect(wrapper.emitted('signedUp')).toEqual([[]]);
    expect(wrapper.text()).toContain('Signing you in');

    await vi.advanceTimersByTimeAsync(10_000);
    expect(polls()).toHaveLength(3);
  });

  it('asks for a sign-in when the person joined but the answer set no session', async () => {
    const { wrapper } = await waiting([status({ status: 'completed', isSignedIn: false })]);

    await vi.advanceTimersByTimeAsync(2000);

    expect(wrapper.emitted('signInRequired')).toEqual([[]]);
    expect(wrapper.emitted('signedUp')).toBeUndefined();
  });

  it('says why a refused attempt failed, and starts again from the kept profile', async () => {
    const { wrapper, polls } = await waiting([
      status({ status: 'failed', failureCode: 'InvitationIdentifierMismatch' }),
    ]);

    await vi.advanceTimersByTimeAsync(2000);
    expect(wrapper.text()).toContain('This invitation is for another phone number.');
    await vi.advanceTimersByTimeAsync(10_000);
    expect(polls()).toHaveLength(1);

    await button(wrapper, 'Start again').trigger('click');
    expect(button(wrapper, 'Verify phone in Telegram').exists()).toBe(true);
  });

  it.each([
    ['InvitationRevoked', 'This invitation was withdrawn.'],
    ['InvitationAccepted', 'This invitation was already used.'],
    ['PhoneCountryNotAllowed', 'Phone numbers from your country cannot sign up here.'],
  ])('says a %s refusal in its own words', async (failureCode, message) => {
    const { wrapper } = await waiting([status({ status: 'failed', failureCode })]);

    await vi.advanceTimersByTimeAsync(2000);

    expect(wrapper.text()).toContain(message);
    expect(wrapper.findAll('button').some((candidate) => candidate.text() === 'Start again')).toBe(
      failureCode === 'PhoneCountryNotAllowed',
    );
  });

  it('says the time ran out when the service answers expired', async () => {
    const { wrapper } = await waiting([status({ status: 'expired' })]);

    await vi.advanceTimersByTimeAsync(2000);

    expect(wrapper.text()).toContain('The time to share your contact ran out.');
  });

  it("stops at the attempt's end, checking once more as it comes", async () => {
    const { wrapper, polls } = await waiting([status({ status: 'pending' })], 5000);

    await vi.advanceTimersByTimeAsync(4999);
    expect(polls()).toHaveLength(2);
    expect(wrapper.text()).toContain('Waiting for your contact');
    await vi.advanceTimersByTimeAsync(1);

    expect(polls()).toHaveLength(3);
    expect(wrapper.text()).toContain('The time to share your contact ran out.');
    await vi.advanceTimersByTimeAsync(10_000);
    expect(polls()).toHaveLength(3);
  });

  it('polls through a failing service, and ends once the attempt is gone', async () => {
    const { wrapper, polls } = await waiting([
      () => problem(503, 'Unavailable'),
      () => problem(404, 'No sign-up is running.', { identityCode: 'SignUpVerificationNotFound' }),
    ]);

    await vi.advanceTimersByTimeAsync(2000);
    expect(wrapper.text()).toContain('Waiting for your contact');
    await vi.advanceTimersByTimeAsync(2000);

    expect(polls()).toHaveLength(2);
    expect(wrapper.text()).toContain('This phone check is no longer running.');
  });

  it('stops polling when the person starts over or the form goes away', async () => {
    const { wrapper, polls } = await waiting([status({ status: 'pending' })]);

    await vi.advanceTimersByTimeAsync(2000);
    await button(wrapper, 'Start over').trigger('click');
    await vi.advanceTimersByTimeAsync(10_000);
    expect(polls()).toHaveLength(1);

    await submit(wrapper);
    await vi.advanceTimersByTimeAsync(2000);
    expect(polls()).toHaveLength(2);
    wrapper.unmount();
    await vi.advanceTimersByTimeAsync(10_000);
    expect(polls()).toHaveLength(2);
  });
});
