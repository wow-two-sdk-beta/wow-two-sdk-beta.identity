/** Mirrors the account API's routes: relative to the service or consumer origin, under the contract's HTTP mirror. */
export const AccountRouteConstants = {
  /** The account API's mount point. */
  base: '/api/account',
  /** `POST`: signs an account up while sign-up is open, or with an invitation's token. */
  signUp: '/sign-up',
  /** `POST`: signs in with an identifier and password. */
  signIn: '/sign-in',
  /** `POST`: completes a sign-in that answered `TwoFactorRequired` with a ticket, by a second-factor or recovery code. */
  signInTwoFactor: '/sign-in/two-factor',
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
  /**
   * `POST`: signs in with a phone and the code sent to it. A phone whose person is another's trusted contact answers `200`
   * with an `accountChoice` and no session; `phoneSignInChoose` then picks the account. A browser new to the account
   * answers `200` with a `deviceConfirmation` and no session; `phoneSignInConfirmation` then waits for a contact's press.
   */
  phoneSignIn: '/phone-sign-in',
  /**
   * `POST`: asks where a new device's confirmation stands, polled with the `token` of a `deviceConfirmation`: `200` with
   * `status` `pending`, then as `phoneSignIn` once a contact approved, or `403` / `401` once denied / ended.
   */
  phoneSignInConfirmation: '/phone-sign-in/confirmation',
  /** `POST`: lists the trusted contacts a phone's sign-in code can go to instead, as masked labels with opaque ids. */
  phoneSignInTargets: '/phone-sign-in/targets',
  /** `POST`: signs in as the account a trusted contact picked from an `accountChoice`, with its token. */
  phoneSignInChoose: '/phone-sign-in/choose',
  /** `GET`, anonymous: an invitation link's preview, `/invitations/{token}`; `POST` `/invitations/{token}/accept` adds the signed-in person. */
  invitations: '/invitations',
  /**
   * `POST`, anonymous, `multipart/form-data`: the segment after `/invitations/{token}` that starts an invitation sign-up
   * with the profile it asks for (`displayName`, `picture`), `?clientId=`. Answers the bot's start link and binds the
   * attempt to the browser by an HttpOnly cookie.
   */
  invitationSignUp: '/sign-up',
  /**
   * `GET`, anonymous: where the invitation sign-up the browser's cookie names stands, `?clientId=`. The first
   * `completed` answer sets the session cookie.
   */
  invitationSignUpStatus: '/invitations/sign-up/status',
  /**
   * `POST`, anonymous: finishes an invitation sign-up verified by the trusted contact (`verification=trusted-contact`)
   * with the code the contact received, `?clientId=`; `204` and the session cookie.
   */
  invitationSignUpVerify: '/invitations/sign-up/verify',
  /** `GET`: the signed-in account's live sessions, with the trusted contact a session went through. */
  manageSessions: '/manage/sessions',
  /** `GET`: the account's own phone and whether this session may change it. */
  managePhone: '/manage/phone',
  /** `POST`: the bot's `add-phone` link that adds or changes the account's own phone. */
  managePhoneTelegramLink: '/manage/phone/telegram-link',
  /** `GET`: the account's trusted contacts; `DELETE /{userId}` revokes one, from a session signed in with the own phone. */
  manageTrustedContacts: '/manage/trusted-contacts',
  /** `GET`: the accounts that trust the signed-in person; `DELETE /{userId}` stops being their contact. */
  manageTrustedBy: '/manage/trusted-by',
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
