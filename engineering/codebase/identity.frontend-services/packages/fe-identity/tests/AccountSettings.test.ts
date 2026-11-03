import { flushPromises } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PhoneSettings, SessionList, TrustedByList, TrustedContactsList } from '../src/kit';
import { fakeAccountApi, noContent, problem } from './FakeAccountApi';
import { button, render } from './Mounting';

const link = 'https://t.me/ocharo_bot?start=ap-1';
const withLink = () => Response.json({ data: { telegramLinkUrl: link } });
const phoneOf = (data: Record<string, unknown>) => () => Response.json({ data });

beforeEach(() => {
  vi.spyOn(window, 'open').mockReturnValue(null);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('phone settings', () => {
  /** Mounts the block over a phone answered in turn by `phones`, the last one repeating. */
  async function mountPhone(phones: ReadonlyArray<Record<string, unknown>>, answers = {}) {
    const queue = [...phones];
    const api = fakeAccountApi({
      'GET /manage/phone': () => phoneOf(queue.length > 1 ? queue.shift()! : queue[0]!)(),
      'POST /manage/phone/telegram-link': withLink,
      ...answers,
    });
    const wrapper = render(PhoneSettings, { props: { client: api.client } });
    await flushPromises();
    return { ...api, wrapper };
  }

  const reads = (received: ReadonlyArray<{ method: string; url: URL }>) =>
    received.filter((request) => request.method === 'GET');

  it('adds a phone: opens the bot, waits for the contact, and shows the saved number', async () => {
    const { wrapper, received } = await mountPhone([
      { isConfirmed: false, canChange: true },
      { isConfirmed: false, canChange: true },
      { phoneHint: '+998*******45', isConfirmed: true, canChange: true },
    ]);

    expect(wrapper.text()).toContain('You have no phone of your own yet.');
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    await button(wrapper, 'Add a phone').trigger('click');
    await flushPromises();

    expect(window.open).toHaveBeenCalledWith(link, '_blank', 'noopener,noreferrer');
    expect(wrapper.get(`a[href="${link}"]`).text()).toBe('Open Telegram');
    expect(wrapper.text()).toContain('Waiting for your contact');
    await vi.advanceTimersByTimeAsync(2000);
    expect(wrapper.text()).toContain('Waiting for your contact');
    await vi.advanceTimersByTimeAsync(2000);

    expect(reads(received)).toHaveLength(3);
    expect(wrapper.text()).toContain('Your phone is saved.');
    expect(wrapper.text()).toContain('Your phone: +998*******45');
    expect(wrapper.text()).not.toContain('Waiting for your contact');
    expect(wrapper.emitted('changed')).toEqual([[{ phoneHint: '+998*******45', isConfirmed: true, canChange: true }]]);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(reads(received)).toHaveLength(3);
  });

  it('changes a phone: waits for another number, not the one it shows', async () => {
    const same = { phoneHint: '+998*******45', isConfirmed: true, canChange: true };
    const { wrapper } = await mountPhone([same, same, { ...same, phoneHint: '+998*******99' }]);

    expect(wrapper.text()).toContain('Your phone: +998*******45');
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    await button(wrapper, 'Change phone').trigger('click');
    await flushPromises();
    await vi.advanceTimersByTimeAsync(2000);
    expect(wrapper.text()).toContain('Waiting for your contact');
    await vi.advanceTimersByTimeAsync(2000);

    expect(wrapper.text()).toContain('Your phone: +998*******99');
    expect(wrapper.text()).toContain('Your phone is saved.');
  });

  it('offers no change outside a session signed in with the own phone, and says why', async () => {
    const { wrapper } = await mountPhone([{ phoneHint: '+998*******45', isConfirmed: true, canChange: false }]);

    expect(wrapper.text()).toContain('Your phone: +998*******45');
    expect(wrapper.text()).toContain('To change your phone, sign in with the phone you have now.');
    expect(wrapper.findAll('button')).toHaveLength(0);
  });

  it('says so when the service refuses the change for the session', async () => {
    const { wrapper } = await mountPhone([{ phoneHint: '+998*******45', isConfirmed: true, canChange: true }], {
      'POST /manage/phone/telegram-link': () =>
        problem(403, 'Sign in with your phone.', { identityCode: 'main_phone_required' }),
    });

    await button(wrapper, 'Change phone').trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('To change your phone, sign in with the phone you have now.');
    expect(window.open).not.toHaveBeenCalled();
    expect(wrapper.text()).not.toContain('Waiting for your contact');
  });

  it('gives up after 5 minutes without the contact, and stops waiting when cancelled', async () => {
    const { wrapper, received } = await mountPhone([{ isConfirmed: false, canChange: true }]);

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    await button(wrapper, 'Add a phone').trigger('click');
    await flushPromises();
    await vi.advanceTimersByTimeAsync(5 * 60 * 1000 + 2000);

    expect(wrapper.text()).toContain('We did not get your contact in time. Try again.');
    expect(wrapper.text()).not.toContain('Waiting for your contact');
    const polled = reads(received).length;

    await button(wrapper, 'Add a phone').trigger('click');
    await flushPromises();
    await button(wrapper, 'Cancel').trigger('click');
    await vi.advanceTimersByTimeAsync(10_000);

    expect(reads(received)).toHaveLength(polled);
    expect(wrapper.find('button').text()).toBe('Add a phone');
  });

  it('stops polling when the block goes away', async () => {
    const { wrapper, received } = await mountPhone([{ isConfirmed: false, canChange: true }]);

    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    await button(wrapper, 'Add a phone').trigger('click');
    await flushPromises();
    wrapper.unmount();
    await vi.advanceTimersByTimeAsync(10_000);

    expect(reads(received)).toHaveLength(1);
  });

  it('shows nothing while the service has no phone route, and the failure of any other answer', async () => {
    const absent = fakeAccountApi({});
    const none = render(PhoneSettings, { props: { client: absent.client } });
    await flushPromises();
    const broken = fakeAccountApi({ 'GET /manage/phone': () => problem(500, 'The phone could not be read.') });
    const failed = render(PhoneSettings, { props: { client: broken.client } });
    await flushPromises();

    expect(none.html()).toBe('<!--v-if-->');
    expect(failed.text()).toContain('The phone could not be read.');
  });
});

describe('trusted contacts list', () => {
  const lola = {
    userId: 'u2',
    displayName: 'Lola',
    phoneHint: '+998*******45',
    isDeliverable: true,
    createdAt: '2026-11-03T08:00:00+00:00',
  };
  const anvar = { ...lola, userId: 'u3', displayName: 'Anvar', isDeliverable: false };

  async function mountContacts(data: Record<string, unknown>, answers = {}, props = {}) {
    const api = fakeAccountApi({ 'GET /manage/trusted-contacts': phoneOf(data), ...answers });
    const wrapper = render(TrustedContactsList, { props: { client: api.client, ...props } });
    await flushPromises();
    return { ...api, wrapper };
  }

  it('lists the contacts with their phones, and marks one that cannot get a code now', async () => {
    const { wrapper } = await mountContacts({ canRevoke: true, items: [lola, anvar] });

    const rows = wrapper.findAll('li');
    expect(rows).toHaveLength(2);
    expect(rows[0]?.text()).toContain('Lola');
    expect(rows[0]?.text()).toContain('+998*******45 · Added Nov 3, 2026');
    expect(rows[0]?.text()).not.toContain('Cannot receive codes now');
    expect(rows[1]?.text()).toContain('Cannot receive codes now');
    expect(wrapper.text()).toContain('full access to your account');
  });

  it('revokes a contact after a confirm step', async () => {
    const { wrapper, received } = await mountContacts(
      { canRevoke: true, items: [lola, anvar] },
      { 'DELETE /manage/trusted-contacts/u2': noContent },
    );

    await button(wrapper.findAll('li')[0]!, 'Revoke').trigger('click');
    expect(wrapper.text()).toContain('Revoke Lola? Sessions started through them end at once.');
    expect(received).toHaveLength(1);
    await button(wrapper, 'Cancel').trigger('click');
    expect(wrapper.text()).not.toContain('Revoke Lola?');
    await button(wrapper.findAll('li')[0]!, 'Revoke').trigger('click');
    await button(wrapper, 'Yes, revoke').trigger('click');
    await flushPromises();

    expect(received[1]).toMatchObject({ method: 'DELETE' });
    expect(received[1]?.url.pathname).toBe('/api/account/manage/trusted-contacts/u2');
    expect(wrapper.findAll('li')).toHaveLength(1);
    expect(wrapper.text()).not.toContain('Lola');
    expect(wrapper.emitted('revoked')).toEqual([['u2']]);
  });

  it('offers no revoke outside a session signed in with the own phone, and says how to get there', async () => {
    const { wrapper } = await mountContacts({ canRevoke: false, items: [lola] });

    expect(wrapper.text()).toContain('Lola');
    expect(wrapper.findAll('button')).toHaveLength(0);
    expect(wrapper.text()).toContain('To revoke a trusted contact, sign in with your own phone.');
  });

  it('keeps the contact and says so when the service refuses the revoke for the session', async () => {
    const { wrapper } = await mountContacts(
      { canRevoke: true, items: [lola] },
      {
        'DELETE /manage/trusted-contacts/u2': () =>
          problem(403, 'Own phone needed.', { identityCode: 'main_phone_required' }),
      },
    );

    await button(wrapper, 'Revoke').trigger('click');
    await button(wrapper, 'Yes, revoke').trigger('click');
    await flushPromises();

    expect(wrapper.text()).toContain('Lola');
    expect(wrapper.text()).toContain('To revoke a trusted contact, sign in with your own phone.');
    expect(wrapper.emitted('revoked')).toBeUndefined();
  });

  it('says the account has none, unless told to stay out of the way', async () => {
    const shown = await mountContacts({ canRevoke: true, items: [] });
    const hidden = await mountContacts({ canRevoke: true, items: [] }, {}, { isEmptyShown: false });

    expect(shown.wrapper.text()).toContain('You have no trusted contacts.');
    expect(hidden.wrapper.html()).toBe('<!--v-if-->');
  });

  it('shows nothing while the service has no trusted contacts route', async () => {
    const api = fakeAccountApi({});
    const wrapper = render(TrustedContactsList, { props: { client: api.client } });
    await flushPromises();

    expect(wrapper.html()).toBe('<!--v-if-->');
  });
});

describe('people who trust you', () => {
  const anvar = { userId: 'u1', displayName: 'Anvar', createdAt: '2026-11-03T08:00:00+00:00', canLeave: true };
  const lola = { ...anvar, userId: 'u2', displayName: 'Lola', canLeave: false };

  async function mountTrustedBy(data: unknown, answers = {}) {
    const api = fakeAccountApi({ 'GET /manage/trusted-by': phoneOf(data as Record<string, unknown>), ...answers });
    const wrapper = render(TrustedByList, { props: { client: api.client } });
    await flushPromises();
    return { ...api, wrapper };
  }

  it('lists the accounts, offering to leave only where the service allows it, with the reason beside the rest', async () => {
    const { wrapper } = await mountTrustedBy([anvar, lola]);

    const rows = wrapper.findAll('li');
    expect(wrapper.text()).toContain('People who trust you');
    expect(rows[0]?.text()).toContain('Anvar');
    expect(rows[0]?.text()).toContain('Since Nov 3, 2026');
    expect(rows[0]?.findAll('button').map((found) => found.text())).toEqual(['Leave']);
    expect(rows[1]?.findAll('button')).toHaveLength(0);
    expect(rows[1]?.text()).toContain('You are the only contact of an account with no phone of its own');
    expect(rows[0]?.text()).not.toContain('You are the only contact');
  });

  it('leaves an account after a confirm step', async () => {
    const { wrapper, received } = await mountTrustedBy([anvar, lola], { 'DELETE /manage/trusted-by/u1': noContent });

    await button(wrapper, 'Leave').trigger('click');
    expect(wrapper.text()).toContain('Stop receiving the sign-in codes of Anvar?');
    await button(wrapper, 'Yes, leave').trigger('click');
    await flushPromises();

    expect(received[1]).toMatchObject({ method: 'DELETE' });
    expect(received[1]?.url.pathname).toBe('/api/account/manage/trusted-by/u1');
    expect(wrapper.findAll('li')).toHaveLength(1);
    expect(wrapper.emitted('left')).toEqual([['u1']]);
  });

  it('keeps the account and says why when the service holds the last contact', async () => {
    const { wrapper } = await mountTrustedBy([anvar], {
      'DELETE /manage/trusted-by/u1': () =>
        problem(409, 'You are the last contact.', { identityCode: 'last_trusted_contact' }),
    });

    await button(wrapper, 'Leave').trigger('click');
    await button(wrapper, 'Yes, leave').trigger('click');
    await flushPromises();

    expect(wrapper.findAll('li')).toHaveLength(1);
    expect(wrapper.text()).toContain('You are the only contact of an account with no phone of its own');
    expect(wrapper.emitted('left')).toBeUndefined();
  });

  it('shows nothing while nobody trusts the person, or the service has no such route', async () => {
    const empty = await mountTrustedBy([]);
    const api = fakeAccountApi({});
    const absent = render(TrustedByList, { props: { client: api.client } });
    await flushPromises();

    expect(empty.wrapper.html()).toBe('<!--v-if-->');
    expect(absent.html()).toBe('<!--v-if-->');
  });
});

describe('session list', () => {
  const session = {
    id: 's1',
    createdAt: '2026-11-03T08:00:00+00:00',
    lastSeenAt: '2026-11-03T09:00:00+00:00',
    expiresAt: '2026-11-10T08:00:00+00:00',
    authenticationMethods: ['otp'],
    ipMasked: '203.0.113.0',
    userAgent: 'Firefox on macOS',
    isCurrent: true,
  };

  it('lists the sessions, marks this one, and names the trusted contact a session went through', async () => {
    const { client } = fakeAccountApi({
      'GET /manage/sessions': () =>
        Response.json({
          data: [
            session,
            { ...session, id: 's2', isCurrent: false, userAgent: null, trustedContactDisplayName: 'Anvar' },
          ],
        }),
    });
    const wrapper = render(SessionList, { props: { client } });
    await flushPromises();

    const rows = wrapper.findAll('li');
    expect(rows[0]?.text()).toContain('Firefox on macOS');
    expect(rows[0]?.text()).toContain('This session');
    expect(rows[0]?.text()).toContain('203.0.113.0');
    expect(rows[0]?.text()).not.toContain('Signed in through');
    expect(rows[1]?.text()).toContain('Unknown device');
    expect(rows[1]?.text()).not.toContain('This session');
    expect(rows[1]?.text()).toContain('Signed in through Anvar');
  });

  it('shows nothing while the service keeps no session list', async () => {
    const { client } = fakeAccountApi({});
    const wrapper = render(SessionList, { props: { client } });
    await flushPromises();

    expect(wrapper.html()).toBe('<!--v-if-->');
  });
});
