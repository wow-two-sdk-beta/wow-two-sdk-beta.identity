import { flushPromises } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { InvitationsPanel } from '../src/admin';
import { noContent, problem } from './FakeAccountApi';
import { fakeOrganizationApi, invitation, member, organizationId } from './FakeOrganizationApi';
import { button, pickOption, render } from './Mounting';

const pending = invitation('i2', 'editor', { phoneNumber: '+998901234567' });
const accepted = invitation('i1', 'viewer', {
  status: 'accepted',
  acceptedAt: '2026-11-02T08:00:00+00:00',
  acceptedByUserId: 'u9',
});
const link = 'https://id.example.test/sign-up?invitation=tok-3';

async function mountPanel(answers: Parameters<typeof fakeOrganizationApi>[0], props: { currentUserId?: string } = {}) {
  const api = fakeOrganizationApi({
    'GET /invitations': () => Response.json({ data: [pending, accepted] }),
    ...answers,
  });
  const wrapper = render(InvitationsPanel, {
    props: { organizationId, client: api.client, countries: ['UZ'], ...props },
  });
  await flushPromises();
  return { ...api, wrapper };
}

const rows = (wrapper: Awaited<ReturnType<typeof mountPanel>>['wrapper']) => wrapper.findAll('li');

/** The requests the invitations themselves made: the form's read of the members for its contact picker is left out. */
const invitationCalls = (received: Awaited<ReturnType<typeof mountPanel>>['received']) =>
  received.filter((request) => !request.url.pathname.endsWith('/members'));

describe('invitations panel', () => {
  it('lists the invitations with where each stands, offering to revoke only a pending one', async () => {
    const { wrapper } = await mountPanel({});

    expect(rows(wrapper)).toHaveLength(2);
    expect(rows(wrapper)[0]?.text()).toContain('+998901234567');
    expect(rows(wrapper)[0]?.text()).toContain('Editor');
    expect(rows(wrapper)[0]?.text()).toContain('Pending');
    expect(rows(wrapper)[0]?.text()).toContain('Expires Nov 8, 2026');
    expect(rows(wrapper)[1]?.text()).toContain('Anyone with the link');
    expect(rows(wrapper)[1]?.text()).toContain('Accepted Nov 2, 2026');
    expect(
      rows(wrapper)[0]
        ?.findAll('button')
        .map((found) => found.text()),
    ).toEqual(['Revoke']);
    expect(rows(wrapper)[1]?.findAll('button')).toHaveLength(0);
  });

  it('invites a phone with a role and shows the link once, with a copy button', async () => {
    const made = invitation('i3', 'admin', { phoneNumber: '+998907654321' });
    const { wrapper, received } = await mountPanel({
      'POST /invitations': () => Response.json({ data: { invitation: made, token: 'tok-3', link } }),
    });

    expect(wrapper.find('select').exists()).toBe(false);
    expect(wrapper.text()).toContain('+998');
    await pickOption(wrapper, 'Role', 'Admin');
    await wrapper.get('input[type="tel"]').setValue('907654321');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(invitationCalls(received)[1]).toMatchObject({
      method: 'POST',
      body: { role: 'admin', phoneNumber: '+998907654321' },
    });
    expect(invitationCalls(received)[1]?.url.pathname).toBe(`/api/organizations/${organizationId}/invitations`);
    expect(wrapper.text()).toContain('Send this link to the person you invite. It shows only this once.');
    expect(wrapper.get('code').text()).toBe(link);
    await wrapper.get('button[aria-label="Copy"]').trigger('click');
    await flushPromises();
    expect(await navigator.clipboard.readText()).toBe(link);
    expect(rows(wrapper)).toHaveLength(3);
    expect(rows(wrapper)[0]?.text()).toContain('+998907654321');
    expect(wrapper.emitted('created')).toEqual([[{ invitation: made, token: 'tok-3', link }]]);
    expect(wrapper.get('button[aria-label="Role"]').text()).toBe('Viewer');
    expect((wrapper.get('input[type="tel"]').element as HTMLInputElement).value).toBe('');

    await button(wrapper, 'Done').trigger('click');
    expect(wrapper.find('code').exists()).toBe(false);
  });

  it('makes a link anyone may redeem, showing the token when the instance names no origin', async () => {
    const made = invitation('i3', 'viewer');
    const { wrapper, received } = await mountPanel({
      'POST /invitations': () => Response.json({ data: { invitation: made, token: 'tok-3', link: null } }),
    });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(invitationCalls(received)[1]?.body).toEqual({ role: 'viewer' });
    expect(wrapper.text()).toContain('Send this invitation token to the person you invite.');
    expect(wrapper.get('code').text()).toBe('tok-3');
  });

  it('keeps an invalid phone in the form', async () => {
    const { wrapper, received } = await mountPanel({});

    await wrapper.get('input[type="tel"]').setValue('9012');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(invitationCalls(received)).toHaveLength(1);
    expect(wrapper.find('code').exists()).toBe(false);
  });

  it('invites an owner like any other role', async () => {
    const made = invitation('i3', 'owner');
    const { wrapper, received } = await mountPanel({
      'POST /invitations': () => Response.json({ data: { invitation: made, token: 'tok-3', link } }),
    });

    await pickOption(wrapper, 'Role', 'Owner');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(invitationCalls(received)[1]?.body).toEqual({ role: 'owner' });
    expect(wrapper.get('code').text()).toBe(link);
    expect(rows(wrapper)[0]?.text()).toContain('Owner');
  });

  it("shows the server's refusal of an invitation", async () => {
    const { wrapper } = await mountPanel({
      'POST /invitations': () =>
        problem(403, 'Only an owner or admin manages the organization’s members.', {
          identityCode: 'OrganizationManagerRequired',
        }),
    });

    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(wrapper.get('form').text()).toContain('Only an owner or admin manages the organization’s members.');
    expect(wrapper.find('code').exists()).toBe(false);
    expect(rows(wrapper)).toHaveLength(2);
  });

  it('revokes a pending invitation', async () => {
    const { wrapper, received } = await mountPanel({ 'DELETE /invitations/i2': noContent });

    await button(rows(wrapper)[0]!, 'Revoke').trigger('click');
    await flushPromises();

    expect(invitationCalls(received)[1]).toMatchObject({ method: 'DELETE' });
    expect(invitationCalls(received)[1]?.url.pathname).toBe(`/api/organizations/${organizationId}/invitations/i2`);
    expect(rows(wrapper)[0]?.text()).toContain('Revoked');
    expect(rows(wrapper)[0]?.findAll('button')).toHaveLength(0);
    expect(wrapper.emitted('revoked')).toEqual([['i2']]);
  });

  it("shows the server's refusal of a revoke under the invitation", async () => {
    const { wrapper } = await mountPanel({
      'DELETE /invitations/i2': () =>
        problem(404, 'The invitation link is not valid.', { identityCode: 'InvitationNotFound' }),
    });

    await button(rows(wrapper)[0]!, 'Revoke').trigger('click');
    await flushPromises();

    expect(rows(wrapper)[0]?.text()).toContain('The invitation link is not valid.');
    expect(rows(wrapper)[0]?.text()).toContain('Pending');
    expect(wrapper.emitted('revoked')).toBeUndefined();
  });
});

describe('invitation trusted contact', () => {
  const ann = member('u1', 'owner', { displayName: 'Ann', isPhoneConfirmed: true });
  const bob = member('u2', 'editor', { displayName: 'Bob', isPhoneConfirmed: true });
  const cy = member('u3', 'viewer', { displayName: 'Cy', isPhoneConfirmed: false });
  const made = invitation('i3', 'viewer');
  const created = {
    'POST /invitations': () => Response.json({ data: { invitation: made, token: 'tok-3', link } }),
  };

  /** The names the open trusted contact select offers, in order. */
  async function offered(wrapper: Awaited<ReturnType<typeof mountPanel>>['wrapper']) {
    await wrapper.get('button[aria-label="Trusted contact"]').trigger('click');
    await flushPromises();
    const names = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].map((option) =>
      option.textContent?.trim(),
    );
    await wrapper.get('button[aria-label="Trusted contact"]').trigger('click');
    await flushPromises();
    return names;
  }

  it('offers no one, me, or a member with a confirmed phone, and nothing without a candidate', async () => {
    const { wrapper } = await mountPanel(
      { 'GET /members': () => Response.json({ data: [ann, bob, cy] }), ...created },
      { currentUserId: 'u1' },
    );

    expect(await offered(wrapper)).toEqual(['No trusted contact', 'Me', 'Bob']);
    expect(wrapper.get('button[aria-label="Trusted contact"]').text()).toBe('No trusted contact');
    expect(wrapper.text()).not.toContain('full access');
    const { wrapper: alone } = await mountPanel({ 'GET /members': () => Response.json({ data: [cy] }) });
    expect(alone.find('button[aria-label="Trusted contact"]').exists()).toBe(false);
  });

  it('leaves out "me" while the inviter’s own phone is not confirmed', async () => {
    const { wrapper } = await mountPanel(
      { 'GET /members': () => Response.json({ data: [{ ...ann, isPhoneConfirmed: false }, bob] }) },
      { currentUserId: 'u1' },
    );

    expect(await offered(wrapper)).toEqual(['No trusted contact', 'Bob']);
  });

  it('invites with the inviter as the trusted contact, warning of the full access it gives', async () => {
    const { wrapper, received } = await mountPanel(
      { 'GET /members': () => Response.json({ data: [ann, bob] }), ...created },
      { currentUserId: 'u1' },
    );

    await pickOption(wrapper, 'Trusted contact', 'Me');

    expect(wrapper.text()).toContain('full access to the account');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(invitationCalls(received).find((request) => request.method === 'POST')?.body).toEqual({
      role: 'viewer',
      trustedContactUserId: 'u1',
    });
    expect(wrapper.get('button[aria-label="Trusted contact"]').text()).toBe('No trusted contact');
    expect(wrapper.text()).not.toContain('full access to the account');
  });

  it('invites with another member as the trusted contact, beside a phone and a role', async () => {
    const { wrapper, received } = await mountPanel(
      { 'GET /members': () => Response.json({ data: [ann, bob] }), ...created },
      { currentUserId: 'u1' },
    );

    await pickOption(wrapper, 'Role', 'Owner');
    await wrapper.get('input[type="tel"]').setValue('907654321');
    await pickOption(wrapper, 'Trusted contact', 'Bob');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(invitationCalls(received).find((request) => request.method === 'POST')?.body).toEqual({
      role: 'owner',
      phoneNumber: '+998907654321',
      trustedContactUserId: 'u2',
    });
  });

  it('invites with no contact once the choice is put back to none', async () => {
    const { wrapper, received } = await mountPanel(
      { 'GET /members': () => Response.json({ data: [ann, bob] }), ...created },
      { currentUserId: 'u1' },
    );

    await pickOption(wrapper, 'Trusted contact', 'Bob');
    await pickOption(wrapper, 'Trusted contact', 'No trusted contact');
    await wrapper.get('form').trigger('submit');
    await flushPromises();

    expect(invitationCalls(received).find((request) => request.method === 'POST')?.body).toEqual({ role: 'viewer' });
  });
});
