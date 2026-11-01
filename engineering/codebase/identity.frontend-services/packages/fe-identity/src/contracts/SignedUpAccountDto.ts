/** Mirrors `SignedUpAccountDto` of the be contract: the account a sign-up created. */
export interface SignedUpAccountDto {
  /** The new user id. */
  readonly userId: string;
  /** Whether a confirmation email was sent. */
  readonly isConfirmationSent: boolean;
}
