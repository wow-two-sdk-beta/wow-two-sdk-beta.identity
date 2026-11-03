/** Mirrors `InvitationApiRequest` of the be contract: makes an invitation link into an organization. */
export interface InvitationApiRequest {
  /** The role the person joins with, one of `OrganizationRoleConstants`, `owner` included. */
  readonly role: string;
  /** The phone number that alone may redeem the link, E.164; omitted for anyone holding it. */
  readonly phoneNumber?: string | null;
  /** The email that alone may redeem the link; omitted for anyone holding it. */
  readonly email?: string | null;
  /**
   * The member who receives the invited person's sign-in codes: the inviter or another member with a confirmed phone.
   * That is full access to the new account, so the picker says so; omitted for none.
   */
  readonly trustedContactUserId?: string | null;
}
