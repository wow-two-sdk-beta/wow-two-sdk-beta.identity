/** Mirrors the be contract's `verification` of an invitation sign-up: who proves the invited person's sign-up. */
export const InviteSignUpVerification = {
  /** The invited person's own Telegram: the bot's start link and a shared contact, the default. */
  Telegram: 'telegram',
  /** The invitation's trusted contact: a code goes to their Telegram and is typed on the page. */
  TrustedContact: 'trusted-contact',
} as const;

/** How an invitation sign-up is verified: `telegram` or `trusted-contact`. */
export type InviteSignUpVerification = (typeof InviteSignUpVerification)[keyof typeof InviteSignUpVerification];
