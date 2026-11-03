/** Mirrors the be contract's trusted contact summary of a member: who receives the member's sign-in codes. */
export interface TrustedContactSummaryDto {
  /** The contact's id. */
  readonly userId: string;
  /** The name the contact goes by; `null` when they set none. */
  readonly displayName: string | null;
}
