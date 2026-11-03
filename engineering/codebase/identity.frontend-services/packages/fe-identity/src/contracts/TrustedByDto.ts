/** Mirrors `TrustedByDto` of the be contract: one account whose sign-in codes the signed-in person receives as its trusted contact. */
export interface TrustedByDto {
  /** The account's id. */
  readonly userId: string;
  /** The name the account's person goes by; `null` when they set none. */
  readonly displayName: string | null;
  /** When the person made this one their contact, ISO 8601. */
  readonly createdAt: string;
  /** Whether the signed-in person may stop being the contact: not the last contact of an account with no phone of its own. */
  readonly canLeave: boolean;
}
