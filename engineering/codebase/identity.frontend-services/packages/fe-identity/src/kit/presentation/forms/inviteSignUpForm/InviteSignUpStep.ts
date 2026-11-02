/** Defines where an invitation sign-up stands on the page. */
export const InviteSignUpStep = {
  /** Refers to giving the profile the instance asks for, then starting the phone check. */
  Profile: 'profile',
  /** Refers to waiting while the person shares the contact with the Telegram bot; the page polls meanwhile. */
  Telegram: 'telegram',
  /** Refers to an attempt that ended without an account: refused, timed out or gone. */
  Ended: 'ended',
  /** Refers to a finished sign-up, once `signedUp` or `signInRequired` fired. */
  Completed: 'completed',
} as const;
export type InviteSignUpStep = (typeof InviteSignUpStep)[keyof typeof InviteSignUpStep];
