/** Mirrors `PasswordChangeLinkApiRequest` of the be contract: a change link's one-time token and the new password. */
export interface PasswordChangeLinkApiRequest {
  /** The one-time token from the link's fragment. */
  readonly token: string;
  /** The replacement password. */
  readonly newPassword: string;
}
