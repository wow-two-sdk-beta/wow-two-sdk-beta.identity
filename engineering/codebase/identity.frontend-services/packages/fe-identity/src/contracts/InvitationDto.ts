import type { InvitationStatus } from './InvitationPreviewDto';

/** Mirrors `InvitationDto` of the be contract: one invitation into an organization, as its owners and admins see it. */
export interface InvitationDto {
  /** The invitation's id. */
  readonly id: string;
  /** The role the person joins with. */
  readonly role: string;
  /** Where it stands. */
  readonly status: InvitationStatus;
  /** The phone number that alone may redeem it; `null` when it names none. */
  readonly phoneNumber: string | null;
  /** The email that alone may redeem it; `null` when it names none. */
  readonly email: string | null;
  /** When it was made, ISO 8601. */
  readonly createdAt: string;
  /** When it stops working, ISO 8601. */
  readonly expiresAt: string;
  /** When a person redeemed it, ISO 8601; `null` while unused. */
  readonly acceptedAt: string | null;
  /** The person who redeemed it; `null` while unused. */
  readonly acceptedByUserId: string | null;
}
