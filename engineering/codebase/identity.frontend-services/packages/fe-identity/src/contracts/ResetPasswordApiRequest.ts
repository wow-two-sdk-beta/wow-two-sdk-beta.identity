/** Mirrors `ResetPasswordApiRequest` of the be contract: resets a forgotten password with the emailed token. */
export interface ResetPasswordApiRequest {
  /** The account email. */
  readonly email: string;
  /** The token from the reset email. */
  readonly resetToken: string;
  /** The replacement password. */
  readonly newPassword: string;
}
