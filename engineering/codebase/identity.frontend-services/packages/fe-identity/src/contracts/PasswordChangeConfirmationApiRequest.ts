/** Mirrors `PasswordChangeConfirmationApiRequest` of the be contract: asks for a change's code or link. */
export interface PasswordChangeConfirmationApiRequest {
  /** The ticket a sign-in answered with when the password breaks the current rules; omitted when signed in. */
  readonly ticket?: string;
}
