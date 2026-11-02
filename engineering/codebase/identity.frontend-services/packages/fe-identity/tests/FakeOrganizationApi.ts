import { vi } from 'vitest';
import { createOrganizationClient } from '../src/admin';
import { problem, serviceUrl, type ReceivedRequest } from './FakeAccountApi';

type Answer = (request: ReceivedRequest) => Response;

export const organizationId = '0f8fad5b-d9cb-469f-a165-70867728950e';

/**
 * Answers each `METHOD /path` (path below `/api/organizations/{organizationId}`) with its handler; anything else is a
 * 404 problem.
 */
export function fakeOrganizationApi(answers: Record<string, Answer>) {
  const received: ReceivedRequest[] = [];
  const fetch = vi.fn<typeof globalThis.fetch>(async (input, init) => {
    const url = new URL(String(input));
    const method = (init?.method ?? 'GET').toUpperCase();
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
    const request = { method, url, body };
    received.push(request);
    const answer = answers[`${method} ${url.pathname.replace(`/api/organizations/${organizationId}`, '')}`];
    return answer ? answer(request) : problem(404, 'Not found');
  });
  return { fetch, received, client: createOrganizationClient({ baseUrl: serviceUrl, fetch }) };
}

export const member = (userId: string, role: string, extra: Record<string, unknown> = {}) => ({
  userId,
  displayName: null,
  userName: null,
  email: null,
  phoneNumber: null,
  role,
  joinedAt: '2026-10-20T08:00:00+00:00',
  ...extra,
});

export const invitation = (id: string, role: string, extra: Record<string, unknown> = {}) => ({
  id,
  role,
  status: 'pending',
  phoneNumber: null,
  email: null,
  createdAt: '2026-11-01T08:00:00+00:00',
  expiresAt: '2026-11-08T08:00:00+00:00',
  acceptedAt: null,
  acceptedByUserId: null,
  ...extra,
});
