/** Mirrors `RegisteredAccountDto` of the be contract: the account a registration created. */
export interface RegisteredAccountDto {
  /** The new user id. */
  readonly userId: string;
  /** Whether a confirmation email was sent. */
  readonly isConfirmationSent: boolean;
}
