/** Defines where a phone sign-in stands. */
export const PhoneSignInStep = {
  /** Refers to entering the phone a code goes to. */
  Phone: 'phone',
  /** Refers to picking where the code goes: the phone's own Telegram or one of its trusted contacts. */
  Target: 'target',
  /** Refers to opening the Telegram bot and sharing the contact, after which the bot sends the code. */
  Telegram: 'telegram',
  /** Refers to entering the code. */
  Code: 'code',
  /** Refers to entering the second factor the account requires once the code checked out. */
  SecondFactor: 'second-factor',
  /** Refers to picking the account a trusted contact signs in as, once the code checked out. */
  Account: 'account',
} as const;
export type PhoneSignInStep = (typeof PhoneSignInStep)[keyof typeof PhoneSignInStep];
