/** Defines where a phone sign-in stands. */
export const PhoneSignInStep = {
  /** Refers to entering the phone a code goes to. */
  Phone: 'phone',
  /** Refers to opening the Telegram bot and sharing the contact, after which the bot sends the code. */
  Telegram: 'telegram',
  /** Refers to entering the code. */
  Code: 'code',
} as const;
export type PhoneSignInStep = (typeof PhoneSignInStep)[keyof typeof PhoneSignInStep];
