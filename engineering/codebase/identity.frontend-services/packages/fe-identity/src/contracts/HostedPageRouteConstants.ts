/** Holds the hosted pages the service serves and the parameters each one reads. */
export const HostedPageRouteConstants = {
  /** Signs a person in by every enabled method, then continues to `returnUrl`. */
  signIn: '/sign-in',
  /** Creates an account while sign-up is open, or from an invitation link (`invitation`). */
  signUp: '/sign-up',
  /** Confirms an email from the emailed link (`userId`, `token`), then offers the sign-in that continues to `returnUrl`. */
  confirmEmail: '/confirm-email',
  /** Sends a password reset email. */
  forgotPassword: '/forgot-password',
  /** Sets a new password from the emailed reset link (`email`, `token`), then offers the sign-in that continues to `returnUrl`. */
  resetPassword: '/reset-password',
  /** Spends a password change link; the token travels in the URL fragment (`#token=`), `returnUrl` in the query. */
  changePassword: '/change-password',
  /** The signed-in person's security settings: the password change. */
  accountSecurity: '/account/security',
  /** The query parameter that carries the local path a page continues to. */
  returnUrlParameter: 'returnUrl',
  /** The query parameter of an OIDC authorization request that names its client. */
  clientIdParameter: 'client_id',
  /** The query parameter that carries an account id. */
  userIdParameter: 'userId',
  /** The query parameter that carries an email address. */
  emailParameter: 'email',
  /** The query or fragment parameter that carries a one-time token. */
  tokenParameter: 'token',
  /** The query parameter of an invitation link that carries its token. */
  invitationParameter: 'invitation',
} as const;
