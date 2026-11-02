/** Mirrors the be contract's invitation sign-up status: where the attempt this browser started stands. */
export const InviteSignUpStatus = {
  /** The bot has not received the contact yet. */
  Pending: 'pending',
  /** The phone is proven and the person joined; `isSignedIn` says whether this browser holds the session. */
  Completed: 'completed',
  /** The contact came but the join was refused; `failureCode` says why. */
  Failed: 'failed',
  /** No contact came before the attempt's end. */
  Expired: 'expired',
} as const;

/** Where an invitation sign-up stands: `pending`, `completed`, `failed` or `expired`. */
export type InviteSignUpStatus = (typeof InviteSignUpStatus)[keyof typeof InviteSignUpStatus];
