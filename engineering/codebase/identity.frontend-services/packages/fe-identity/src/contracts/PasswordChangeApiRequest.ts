/** Mirrors `PasswordChangeApiRequest` of the be contract: changes the password of a signed-in or ticketed person. */
export interface PasswordChangeApiRequest {
  /** The current password; not asked of a person holding the ticket, whose sign-in just verified it. */
  readonly currentPassword?: string;
  /** The replacement password. */
  readonly newPassword: string;
  /** The one-time code sent under the `Code` confirmation. */
  readonly code?: string;
  /** The ticket a sign-in answered with when the password breaks the current rules; omitted when signed in. */
  readonly ticket?: string;
}
