/** Mirrors the be contract's account choice entry: one account a trusted contact can sign in as. */
export interface PhoneSignInAccountDto {
  /** The account's id. */
  readonly userId: string;
  /** The name the account's person goes by; `null` when they set none. */
  readonly displayName: string | null;
  /** The account's picture, a same-origin path or an HTTPS URL; `null` without one. */
  readonly pictureUrl: string | null;
  /** Whether this is the contact's own account, as opposed to an account that trusts them. */
  readonly isSelf: boolean;
}
