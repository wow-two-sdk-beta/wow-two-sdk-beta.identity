import type { TrustedContactDto } from './TrustedContactDto';

/** Mirrors `TrustedContactsDto` of the be contract: the account's trusted contacts and whether this session may revoke them. */
export interface TrustedContactsDto {
  /** Whether this session was signed in with the account's own phone, which alone revokes a contact. */
  readonly canRevoke: boolean;
  /** The contacts, in the service's order. */
  readonly items: ReadonlyArray<TrustedContactDto>;
}
