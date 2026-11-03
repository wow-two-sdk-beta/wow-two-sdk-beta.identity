/** Mirrors `AccountPhoneDto` of the be contract: the signed-in account's own phone and whether this session may change it. */
export interface AccountPhoneDto {
  /** A hint of the account's phone number, such as `+998*******45`; `null` without one. */
  readonly phoneHint: string | null;
  /** Whether the phone is confirmed. */
  readonly isConfirmed: boolean;
  /** Whether this session may change the phone: one signed in with the own phone, or an account that has none. */
  readonly canChange: boolean;
}
