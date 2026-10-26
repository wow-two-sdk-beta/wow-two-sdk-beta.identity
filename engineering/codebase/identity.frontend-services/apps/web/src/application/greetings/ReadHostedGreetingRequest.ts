import { GreetingRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import type { HostedGreetingRequest } from '../../domain/greetings/HostedGreetingRequest';

/** Reads the hosted page's request from its route query; only an absolute http(s) return URL is kept. */
export function readHostedGreetingRequest(query: Readonly<Record<string, unknown>>): HostedGreetingRequest {
  return {
    name: text(query[GreetingRouteConstants.nameParameter]) ?? '',
    returnUrl: safeReturnUrl(text(query[GreetingRouteConstants.returnUrlParameter])),
  };
}

/** Returns a query value as text: the value itself, or the first text of a repeated parameter. */
function text(value: unknown): string | null {
  if (typeof value === 'string') return value;
  return Array.isArray(value) ? (value.find((item): item is string => typeof item === 'string') ?? null) : null;
}

/** Keeps an absolute http(s) URL; a real domain also checks it against its registered apps. */
function safeReturnUrl(value: string | null): string | null {
  if (!value || !URL.canParse(value)) return null;
  const url = new URL(value);
  return url.protocol === 'https:' || url.protocol === 'http:' ? url.toString() : null;
}
