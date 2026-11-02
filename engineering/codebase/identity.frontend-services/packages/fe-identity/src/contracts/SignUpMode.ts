/** Mirrors the be contract's sign-up mode: who may create an account. */
export const SignUpMode = {
  /** Only through an invitation link: the pages offer no sign-up without one. */
  Invite: 'invite',
  /** Anyone. */
  Open: 'open',
} as const;

/** Who may create an account, `invite` or `open`. */
export type SignUpMode = (typeof SignUpMode)[keyof typeof SignUpMode];
