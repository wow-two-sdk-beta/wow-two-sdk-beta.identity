/** Defines where a phone sign-in stands. */
export const PhoneSignInStep = {
  /** Refers to entering the phone a code goes to. */
  Phone: 'phone',
  /** Refers to opening the Telegram bot and sharing the contact, after which the bot sends the code. */
  Telegram: 'telegram',
  /** Refers to entering the code. */
  Code: 'code',
  /** Refers to entering the second factor the account requires once the code checked out. */
  SecondFactor: 'second-factor',
} as const;
export type PhoneSignInStep = (typeof PhoneSignInStep)[keyof typeof PhoneSignInStep];
