/**
 * Mirrors the `IdentityErrorCodeConstants` the kit acts on: the code a refused account call carries in its problem's
 * `identityCode` extension, beside the message.
 */
export const IdentityErrorCodeConstants = {
  /** The credentials were right but the email is not confirmed yet; a confirmation email can go out again. */
  emailConfirmationRequired: 'EmailConfirmationRequired',
  /** The credentials were right but the phone number is not confirmed yet. */
  phoneConfirmationRequired: 'PhoneConfirmationRequired',
  /** The credentials were right but the account may not sign in yet, for a reason other than a confirmation. */
  signInNotAllowed: 'SignInNotAllowed',
  /** The first factor was right; a second-factor code or a recovery code completes the sign-in. */
  twoFactorRequired: 'TwoFactorRequired',
} as const;
