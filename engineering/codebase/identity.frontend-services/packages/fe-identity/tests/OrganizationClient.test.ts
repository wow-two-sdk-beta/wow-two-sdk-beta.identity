import { describe, expect, it, vi } from 'vitest';
import { createOrganizationClient } from '../src/admin';
import { IdentityErrorCodeConstants } from '../src/contracts';
import { readIdentityCode } from '../src/kit';
import { noContent, problem, serviceUrl } from './FakeAccountApi';
import { fakeOrganizationApi, invitation, member, organizationId } from './FakeOrganizationApi';

const base = `${serviceUrl}/api/organizations/${organizationId}`;

describe('organization client', () => {
  it('lists the members with their roles, by cookie on the service origin', async () => {
    const ann = member('u1', 'owner', { displayName: 'Ann', email: 'ann@example.test', phoneNumber: '+998901234567' });
    const { client, fetch, received } = fakeOrganizationApi({
      'GET /members': () => Response.json({ data: [ann, { userId: 'u2', role: 'viewer', joinedAt: 'x' }] }),
    });

    const result = await client.listMembers(organizationId);

    expect(result).toEqual({
      ok: true,
      value: [
        ann,
        {
          userId: 'u2',
          displayName: null,
          userName: null,
          email: null,
          phoneNumber: null,
          role: 'viewer',
          joinedAt: 'x',
        },
      ],
    });
    expect(received[0]?.url.toString()).toBe(`${base}/members`);
    expect(fetch.mock.calls[0]?.[1]?.credentials).toBe('include');
  });

  it('calls its own origin when no base URL is given', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => Response.json({ data: [] }));

    await createOrganizationClient({ fetch }).listInvitations('org 1');

    expect(String(fetch.mock.calls[0]?.[0])).toBe('/api/organizations/org%201/invitations');
    expect(fetch.mock.calls[0]?.[1]?.credentials).not.toBe('include');
  });

  it.each([
    ['a member without a role', 'GET /members', [{ userId: 'u1', joinedAt: 'x' }]],
    ['an invitation with an unknown status', 'GET /invitations', [invitation('i1', 'editor', { status: 'lost' })]],
    ['a list that is no list', 'GET /members', { userId: 'u1', role: 'owner' }],
  ])('refuses %s', async (_case, route, data) => {
    const { client } = fakeOrganizationApi({ [route]: () => Response.json({ data }) });

    const result =
      route === 'GET /members'
        ? await client.listMembers(organizationId)
        : await client.listInvitations(organizationId);

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.failure.code).toBe('protocol');
  });

  it('re-roles and removes a member', async () => {
    const { client, received } = fakeOrganizationApi({
      'PUT /members/u2': noContent,
      'DELETE /members/u2': noContent,
    });

    expect(await client.changeMemberRole(organizationId, 'u2', { role: 'admin' })).toEqual({
      ok: true,
      value: undefined,
    });
    expect(await client.removeMember(organizationId, 'u2')).toEqual({ ok: true, value: undefined });
    expect(received.map((request) => [request.method, request.url.toString(), request.body])).toEqual([
      ['PUT', `${base}/members/u2`, { role: 'admin' }],
      ['DELETE', `${base}/members/u2`, undefined],
    ]);
  });

  it('makes an invitation and reads its link once', async () => {
    const made = invitation('i1', 'editor', { phoneNumber: '+998901234567' });
    const { client, received } = fakeOrganizationApi({
      'POST /invitations': () =>
        Response.json({
          data: { invitation: made, token: 'tok-1', link: 'https://id.example.test/sign-up?invitation=tok-1' },
        }),
    });

    const result = await client.createInvitation(organizationId, { role: 'editor', phoneNumber: '+998901234567' });

    expect(result).toEqual({
      ok: true,
      value: { invitation: made, token: 'tok-1', link: 'https://id.example.test/sign-up?invitation=tok-1' },
    });
    expect(received[0]?.body).toEqual({ role: 'editor', phoneNumber: '+998901234567' });
  });

  it('reads a missing link as none and refuses an answer without its token', async () => {
    const made = invitation('i1', 'viewer');
    const answers = [
      { invitation: made, token: 'tok-1', link: '' },
      { invitation: made, link: null },
    ];
    const { client } = fakeOrganizationApi({ 'POST /invitations': () => Response.json({ data: answers.shift() }) });

    expect(await client.createInvitation(organizationId, { role: 'viewer' })).toEqual({
      ok: true,
      value: { invitation: made, token: 'tok-1', link: null },
    });
    const refused = await client.createInvitation(organizationId, { role: 'viewer' });
    expect(refused.ok).toBe(false);
    if (!refused.ok) expect(refused.failure.code).toBe('protocol');
  });

  it('lists the invitations and revokes one', async () => {
    const listed = [
      invitation('i2', 'admin'),
      invitation('i1', 'viewer', { status: 'accepted', acceptedAt: 'y', acceptedByUserId: 'u9' }),
    ];
    const { client, received } = fakeOrganizationApi({
      'GET /invitations': () => Response.json({ data: listed }),
      'DELETE /invitations/i2': noContent,
    });

    expect(await client.listInvitations(organizationId)).toEqual({ ok: true, value: listed });
    expect(await client.revokeInvitation(organizationId, 'i2')).toEqual({ ok: true, value: undefined });
    expect(received[1]?.url.toString()).toBe(`${base}/invitations/i2`);
  });

  it("keeps a refusal's problem and identity code", async () => {
    const { client } = fakeOrganizationApi({
      'PUT /members/u1': () =>
        problem(403, 'Only an owner invites, promotes, demotes or removes an owner.', {
          identityCode: 'OrganizationOwnerRequired',
        }),
    });

    const result = await client.changeMemberRole(organizationId, 'u1', { role: 'editor' });

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.failure.status).toBe(403);
    expect(readIdentityCode(result.failure)).toBe(IdentityErrorCodeConstants.organizationOwnerRequired);
  });
});
