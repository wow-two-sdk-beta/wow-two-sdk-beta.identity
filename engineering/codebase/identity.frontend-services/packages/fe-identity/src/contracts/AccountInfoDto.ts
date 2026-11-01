/** Mirrors `AccountInfoDto` of the be contract: the signed-in account. */
export interface AccountInfoDto {
  /** The user id. */
  readonly userId: string;
  /** The login name. */
  readonly userName: string | null;
  /** The email address. */
  readonly email: string | null;
  /** Whether the email is confirmed. */
  readonly isEmailConfirmed: boolean;
  /** Whether two-factor authentication is enabled. */
  readonly isTwoFactorEnabled: boolean;
}
