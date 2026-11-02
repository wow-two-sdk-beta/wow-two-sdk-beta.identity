import { flushPromises } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { MembersPanel } from '../src/admin';
import { noContent, problem } from './FakeAccountApi';
import { fakeOrganizationApi, member, organizationId } from './FakeOrganizationApi';
import { button, pickOption, render } from './Mounting';

const ann = member('u1', 'owner', { displayName: 'Ann', email: 'ann@example.test' });
const bob = member('u2', 'editor', { userName: 'bob', phoneNumber: '+998901234567' });

async function mountPanel(answers: Parameters<typeof fakeOrganizationApi>[0]) {
  const api = fakeOrganizationApi({ 'GET /members': () => Response.json({ data: [ann, bob] }), ...answers });
  const wrapper = render(MembersPanel, { props: { organizationId, client: api.client } });
  await flushPromises();
  return { ...api, wrapper };
}

const row = (wrapper: Awaited<ReturnType<typeof mountPanel>>['wrapper'], name: string) => {
  const found = wrapper.findAll('li').find((candidate) => candidate.find('p').text() === name);
  if (!found) throw new Error(`No member "${name}" in: ${wrapper.text()}`);
  return found;
};

describe('members panel', () => {
  it('lists the members with their contacts and roles', async () => {
    const { wrapper } = await mountPanel({});

    expect(wrapper.findAll('li')).toHaveLength(2);
    expect(row(wrapper, 'Ann').text()).toContain('ann@example.test');
    expect(row(wrapper, 'bob').text()).toContain('+998901234567');
    expect(wrapper.get('button[aria-label="Role of Ann"]').text()).toBe('Owner');
    expect(wrapper.get('button[aria-label="Role of bob"]').text()).toBe('Editor');
  });

  it('says so when the organization has no members', async () => {
    const { wrapper } = await mountPanel({ 'GET /members': () => Response.json({ data: [] }) });

    expect(wrapper.text()).toContain('No members yet.');
  });

  it("shows the server's refusal when the list is not the caller's to read, and loads again on request", async () => {
    const answers = [
      () =>
        problem(403, 'Only an owner or admin manages the organization’s members.', {
          identityCode: 'OrganizationManagerRequired',
        }),
      () => Response.json({ data: [bob] }),
    ];
    const { wrapper } = await mountPanel({ 'GET /members': () => answers.shift()!() });

    expect(wrapper.text()).toContain('Only an owner or admin manages the organization’s members.');
    await button(wrapper, 'Reload').trigger('click');
    await flushPromises();

    expect(wrapper.text()).not.toContain('Only an owner or admin');
    expect(wrapper.findAll('li')).toHaveLength(1);
  });

  it('re-roles a member through the catalog', async () => {
    const { wrapper, received } = await mountPanel({ 'PUT /members/u2': noContent });

    await pickOption(wrapper, 'Role of bob', 'Admin');

    expect(received[1]).toMatchObject({ method: 'PUT', body: { role: 'admin' } });
    expect(received[1]?.url.pathname).toBe(`/api/organizations/${organizationId}/members/u2`);
    expect(wrapper.get('button[aria-label="Role of bob"]').text()).toBe('Admin');
    expect(wrapper.emitted('roleChanged')).toEqual([[{ ...bob, role: 'admin' }]]);
  });

  it("keeps the role and shows the server's refusal under the member", async () => {
    const { wrapper } = await mountPanel({
      'PUT /members/u1': () =>
        problem(403, 'Only an owner invites, promotes, demotes or removes an owner.', {
          identityCode: 'OrganizationOwnerRequired',
        }),
    });

    await pickOption(wrapper, 'Role of Ann', 'Viewer');

    expect(row(wrapper, 'Ann').text()).toContain('Only an owner invites, promotes, demotes or removes an owner.');
    expect(row(wrapper, 'bob').text()).not.toContain('Only an owner');
    expect(wrapper.get('button[aria-label="Role of Ann"]').text()).toBe('Owner');
    expect(wrapper.emitted('roleChanged')).toBeUndefined();
  });

  it('removes a member only after the confirm step', async () => {
    const { wrapper, received } = await mountPanel({ 'DELETE /members/u2': noContent });

    await button(row(wrapper, 'bob'), 'Remove').trigger('click');
    expect(row(wrapper, 'bob').text()).toContain('Remove bob from the organization? Their sessions end at once.');
    await button(row(wrapper, 'bob'), 'Cancel').trigger('click');
    expect(row(wrapper, 'bob').text()).not.toContain('Their sessions end');
    expect(received).toHaveLength(1);

    await button(row(wrapper, 'bob'), 'Remove').trigger('click');
    await button(row(wrapper, 'bob'), 'Yes, remove').trigger('click');
    await flushPromises();

    expect(received[1]).toMatchObject({ method: 'DELETE' });
    expect(received[1]?.url.pathname).toBe(`/api/organizations/${organizationId}/members/u2`);
    expect(wrapper.findAll('li')).toHaveLength(1);
    expect(wrapper.text()).not.toContain('bob');
    expect(wrapper.emitted('removed')).toEqual([['u2']]);
  });

  it('shows a pending Telegram account change and approves it', async () => {
    const lola = member('u3', 'editor', { displayName: 'Lola', hasPendingTelegramChange: true });
    const { wrapper, received } = await mountPanel({
      'GET /members': () => Response.json({ data: [ann, lola] }),
      'POST /members/u3/telegram-change/approve': noContent,
    });

    expect(row(wrapper, 'Lola').text()).toContain('Telegram account change pending');
    expect(row(wrapper, 'Ann').text()).not.toContain('Telegram account change pending');
    await button(row(wrapper, 'Lola'), 'Approve').trigger('click');
    await flushPromises();

    expect(received[1]).toMatchObject({ method: 'POST' });
    expect(received[1]?.url.pathname).toBe(`/api/organizations/${organizationId}/members/u3/telegram-change/approve`);
    expect(row(wrapper, 'Lola').text()).not.toContain('Telegram account change pending');
    expect(wrapper.emitted('telegramChangeResolved')).toEqual([['u3', true]]);
  });

  it('denies a pending Telegram account change', async () => {
    const lola = member('u3', 'editor', { displayName: 'Lola', hasPendingTelegramChange: true });
    const { wrapper, received } = await mountPanel({
      'GET /members': () => Response.json({ data: [lola] }),
      'POST /members/u3/telegram-change/deny': noContent,
    });

    await button(row(wrapper, 'Lola'), 'Deny').trigger('click');
    await flushPromises();

    expect(received[1]?.url.pathname).toBe(`/api/organizations/${organizationId}/members/u3/telegram-change/deny`);
    expect(row(wrapper, 'Lola').text()).not.toContain('Telegram account change pending');
    expect(wrapper.emitted('telegramChangeResolved')).toEqual([['u3', false]]);
  });

  it("keeps the pending change and shows the server's refusal under the member", async () => {
    const lola = member('u3', 'owner', { displayName: 'Lola', hasPendingTelegramChange: true });
    const { wrapper } = await mountPanel({
      'GET /members': () => Response.json({ data: [lola] }),
      'POST /members/u3/telegram-change/approve': () =>
        problem(403, 'Only an owner approves or denies an owner’s Telegram account change.', {
          identityCode: 'OrganizationOwnerRequired',
        }),
    });

    await button(row(wrapper, 'Lola'), 'Approve').trigger('click');
    await flushPromises();

    expect(row(wrapper, 'Lola').text()).toContain('Telegram account change pending');
    expect(row(wrapper, 'Lola').text()).toContain(
      'Only an owner approves or denies an owner’s Telegram account change.',
    );
    expect(wrapper.emitted('telegramChangeResolved')).toBeUndefined();
  });

  it('keeps the last owner and says why', async () => {
    const { wrapper } = await mountPanel({
      'DELETE /members/u1': () =>
        problem(409, 'The organization needs at least one owner.', { identityCode: 'LastOrganizationOwner' }),
    });

    await button(row(wrapper, 'Ann'), 'Remove').trigger('click');
    await button(row(wrapper, 'Ann'), 'Yes, remove').trigger('click');
    await flushPromises();

    expect(wrapper.findAll('li')).toHaveLength(2);
    expect(row(wrapper, 'Ann').text()).toContain('The organization needs at least one owner.');
    expect(wrapper.emitted('removed')).toBeUndefined();
  });
});
