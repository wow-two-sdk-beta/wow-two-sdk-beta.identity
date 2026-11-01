/** Mirrors `SignInMethodIdConstants` of the be contract: the method ids the declaration lists, one kit component each. */
export const SignInMethodIdConstants = {
  /** An identifier and password (`PasswordSignIn`). */
  password: 'password',
  /** A code sent to a phone's linked channel, the instance's Telegram bot (`PhoneCodeSignIn`). */
  phoneCode: 'phone-code',
} as const;
