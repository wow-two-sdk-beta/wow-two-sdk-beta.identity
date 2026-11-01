import { describe, expect, it } from 'vitest';
import {
  hostedHref,
  readChangePasswordToken,
  readConfirmEmailRequest,
  readResetPasswordRequest,
  readSignInRequest,
} from '../src/application/account';

describe('sign-in request', () => {
  it('keeps a local return URL and reads the client it authorizes', () => {
    const returnUrl = '/connect/authorize?client_id=desk&redirect_uri=https%3A%2F%2Fdesk.test%2Fcb&state=x';

    expect(readSignInRequest({ returnUrl })).toEqual({ returnUrl, clientId: 'desk' });
  });

  it('takes the first value of a repeated parameter', () => {
    expect(readSignInRequest({ returnUrl: [null, '/account/security', '/other'] })).toEqual({
      returnUrl: '/account/security',
      clientId: null,
    });
  });

  it.each(['//evil.test/x', 'https://evil.test/x', '/\\evil.test', 'javascript:alert(1)', 'account', '/a\u0000b', ''])(
    'continues to / instead of %j',
    (returnUrl) => {
      expect(readSignInRequest({ returnUrl })).toEqual({ returnUrl: '/', clientId: null });
    },
  );
});

describe('link requests', () => {
  it('reads an email confirmation link and refuses an incomplete one', () => {
    expect(readConfirmEmailRequest({ userId: 'u1', token: 't' })).toEqual({ userId: 'u1', token: 't' });
    expect(readConfirmEmailRequest({ userId: 'u1' })).toBeNull();
  });

  it('reads a reset link and refuses an incomplete one', () => {
    expect(readResetPasswordRequest({ email: 'ann@example.test', token: 't' })).toEqual({
      email: 'ann@example.test',
      token: 't',
    });
    expect(readResetPasswordRequest({ token: 't' })).toBeNull();
  });

  it('reads the change token from the fragment only', () => {
    expect(readChangePasswordToken('#token=abc%2Bdef')).toBe('abc+def');
    expect(readChangePasswordToken('')).toBeNull();
  });

  it('keeps the return URL on links between hosted pages', () => {
    expect(hostedHref('/sign-up', '/')).toBe('/sign-up');
    expect(hostedHref('/sign-up', '/connect/authorize?client_id=desk')).toBe(
      '/sign-up?returnUrl=%2Fconnect%2Fauthorize%3Fclient_id%3Ddesk',
    );
  });
});
