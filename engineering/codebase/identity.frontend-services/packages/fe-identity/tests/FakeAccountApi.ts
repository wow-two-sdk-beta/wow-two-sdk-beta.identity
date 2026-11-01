import { vi } from 'vitest';
import { createAccountClient } from '../src/kit';

/** One request the fake API received: its method, path with query, and parsed JSON body. */
export interface ReceivedRequest {
  readonly method: string;
  readonly url: URL;
  readonly body: unknown;
}

type Answer = (request: ReceivedRequest) => Response;

export const serviceUrl = 'https://identity.example.test';

/** A problem-details answer, as the account API writes one. */
export function problem(status: number, detail: string, extra: Record<string, unknown> = {}) {
  return Response.json(
    { type: 'urn:wow-two:error', title: 'Failed', status, detail, code: 'Failed', ...extra },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );
}

/** Answers each `METHOD /path` (path below `/api/account`) with its handler; anything else is a 404 problem. */
export function fakeAccountApi(answers: Record<string, Answer>) {
  const received: ReceivedRequest[] = [];
  const fetch = vi.fn<typeof globalThis.fetch>(async (input, init) => {
    const url = new URL(String(input));
    const method = (init?.method ?? 'GET').toUpperCase();
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
    const request = { method, url, body };
    received.push(request);
    const answer = answers[`${method} ${url.pathname.replace('/api/account', '')}`];
    return answer ? answer(request) : problem(404, 'Not found');
  });
  return { fetch, received, client: createAccountClient({ baseUrl: serviceUrl, fetch }) };
}

export const noContent = () => new Response(null, { status: 204 });
