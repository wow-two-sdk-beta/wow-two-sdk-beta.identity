import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import type { SignInRequest } from '../../domain/account';

type RouteQuery = Readonly<Record<string, unknown>>;

/** Returns a query value as text: the value itself, or the first text of a repeated parameter. */
function text(value: unknown): string | null {
  if (typeof value === 'string') return value;
  return Array.isArray(value) ? (value.find((item): item is string => typeof item === 'string') ?? null) : null;
}

/** Reads a query parameter as non-blank text. */
function filled(query: RouteQuery, name: string): string | null {
  const value = text(query[name])?.trim();
  return value ? value : null;
}

/**
 * Keeps a path on this origin: it starts with one `/`, never `//` or a backslash that a browser reads as another
 * host, and carries no control characters.
 */
export function readLocalPath(value: string | null): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.includes('\\')) return null;
  return [...value].some((character) => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127) ? null : value;
}

/** Reads where a hosted page continues to and, when that is an authorization request, the app it serves. */
export function readSignInRequest(query: RouteQuery): SignInRequest {
  const returnUrl = readLocalPath(text(query[HostedPageRouteConstants.returnUrlParameter]));
  if (!returnUrl) return { returnUrl: '/', clientId: null };
  const clientId = new URL(returnUrl, 'https://identity.invalid').searchParams.get(
    HostedPageRouteConstants.clientIdParameter,
  );
  return { returnUrl, clientId: clientId || null };
}

/** Reads the account and token an email confirmation link carries; `null` when either is missing. */
export function readConfirmEmailRequest(query: RouteQuery) {
  const userId = filled(query, HostedPageRouteConstants.userIdParameter);
  const token = filled(query, HostedPageRouteConstants.tokenParameter);
  return userId && token ? { userId, token } : null;
}

/** Reads the email and token a password reset link carries; `null` when either is missing. */
export function readResetPasswordRequest(query: RouteQuery) {
  const email = filled(query, HostedPageRouteConstants.emailParameter);
  const token = filled(query, HostedPageRouteConstants.tokenParameter);
  return email && token ? { email, token } : null;
}

/** Reads the one-time token a password change link carries in its fragment (`#token=`). */
export function readChangePasswordToken(hash: string): string | null {
  const token = new URLSearchParams(hash.replace(/^#/, '')).get(HostedPageRouteConstants.tokenParameter)?.trim();
  return token ? token : null;
}

/** Builds a hosted page's link that keeps the return URL, which `/` needs no parameter for. */
export function hostedHref(path: string, returnUrl: string): string {
  return returnUrl === '/'
    ? path
    : `${path}?${new URLSearchParams({ [HostedPageRouteConstants.returnUrlParameter]: returnUrl })}`;
}
