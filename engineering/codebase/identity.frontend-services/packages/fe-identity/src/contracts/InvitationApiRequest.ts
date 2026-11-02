/** Mirrors `InvitationApiRequest` of the be contract: makes an invitation link into an organization. */
export interface InvitationApiRequest {
  /** The role the person joins with, one of `OrganizationRoleConstants`; only an owner invites an owner. */
  readonly role: string;
  /** The phone number that alone may redeem the link, E.164; omitted for anyone holding it. */
  readonly phoneNumber?: string | null;
  /** The email that alone may redeem the link; omitted for anyone holding it. */
  readonly email?: string | null;
}
