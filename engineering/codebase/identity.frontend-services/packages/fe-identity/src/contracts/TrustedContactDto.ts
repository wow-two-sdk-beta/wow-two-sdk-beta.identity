/** Mirrors `TrustedContactDto` of the be contract: one person who receives the signed-in account's sign-in codes. */
export interface TrustedContactDto {
  /** The contact's id. */
  readonly userId: string;
  /** The name the contact goes by; `null` when they set none. */
  readonly displayName: string | null;
  /** A hint of the contact's phone number, such as `+998*******45`; `null` when the service gives none. */
  readonly phoneHint: string | null;
  /** Whether the contact's phone is confirmed and linked on the current bot, so a code can reach them now. */
  readonly isDeliverable: boolean;
  /** When the contact was added, ISO 8601. */
  readonly createdAt: string;
}
