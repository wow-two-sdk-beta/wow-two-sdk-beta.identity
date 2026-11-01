/** Mirrors the account API's routes: relative to the service or consumer origin, under the contract's HTTP mirror. */
export const AccountRouteConstants = {
  /** The account API's mount point. */
  base: '/api/account',
  /** `POST`: signs an account up while sign-up is on. */
  signUp: '/sign-up',
  /** `POST`: signs in with an identifier and password. */
  signIn: '/sign-in',
  /** `GET`: confirms an email with the emailed `userId` and `token`. */
  confirmEmail: '/confirm-email',
  /** `POST`: sends the confirmation email again; always `204`. */
  resendConfirmationEmail: '/resend-confirmation-email',
  /** `POST`: sends a password reset email; always `204`. */
  forgotPassword: '/forgot-password',
  /** `POST`: sets a new password with the emailed reset token. */
  resetPassword: '/reset-password',
  /** `GET`: the signed-in account. */
  manageInfo: '/manage/info',
  /** `POST`: starts a password change, signed in or with a ticket, and answers how it is confirmed. */
  managePasswordConfirmation: '/manage/password/confirmation',
  /** `POST`: changes the password of the signed-in person or of the one a ticket names. */
  managePassword: '/manage/password',
  /** `POST`: changes a password with the one-time token a change link carries; anonymous. */
  passwordChange: '/password/change',
  /** `POST`: sends a phone sign-in code. */
  phoneSignInSend: '/phone-sign-in/send',
  /** `POST`: signs in with a phone and the code sent to it. */
  phoneSignIn: '/phone-sign-in',
  /** `POST`: ends the cookie's session. */
  signOut: '/sign-out',
  /** `GET`, anonymous: the sign-in declaration, the look and the enabled methods with their rules; `?clientId=`, `?locale=`. */
  signInOptions: '/sign-in-options',
  /** The query parameter that asks a sign-in route for a cookie instead of a bearer session. */
  useCookiesParameter: 'useCookies',
  /** The query parameter that names the OIDC client a sign-in serves, for the sign-in log; the look is the instance's. */
  clientIdParameter: 'clientId',
  /** The query parameter that picks the locale of the declaration's messages: `uz`, `ru` or `en`. */
  localeParameter: 'locale',
} as const;
