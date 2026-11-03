/** Where an invitation stands; only a `pending` one redeems. */
export type InvitationStatus = 'pending' | 'expired' | 'accepted' | 'revoked';

/** Mirrors `InvitationPreviewDto` of the be contract: what an invitation link offers, before anyone signs up. */
export interface InvitationPreviewDto {
  /** The organization the person joins. */
  readonly organizationName: string;
  /** The role the person joins with; the link fixes it. */
  readonly role: string;
  /** Where the invitation stands. */
  readonly status: InvitationStatus;
  /** A hint of the phone number that alone may redeem it, such as `+998*******45`; `null` when it names none. */
  readonly phoneNumberHint: string | null;
  /** A hint of the email that alone may redeem it, such as `a***@example.com`; `null` when it names none. */
  readonly emailHint: string | null;
  /** The invitation's trusted contact, as a masked name; `null` when the inviter named none. */
  readonly trustedContactHint: string | null;
  /** When it stops working, ISO 8601. */
  readonly expiresAt: string;
}
