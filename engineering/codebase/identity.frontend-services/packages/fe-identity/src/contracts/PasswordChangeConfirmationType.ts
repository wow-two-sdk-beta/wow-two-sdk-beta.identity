/** Mirrors `PasswordChangeConfirmationType` of the be contract: how a password change is confirmed. */
export const PasswordChangeConfirmationType = {
  /** The current password is the only proof; the change form opens at once. */
  None: 'None',
  /** A one-time code, sent to a verified channel, is entered beside the new password. */
  Code: 'Code',
  /** A one-time link, sent to a verified channel, is the only way to the change form. */
  Link: 'Link',
} as const;
export type PasswordChangeConfirmationType =
  (typeof PasswordChangeConfirmationType)[keyof typeof PasswordChangeConfirmationType];
