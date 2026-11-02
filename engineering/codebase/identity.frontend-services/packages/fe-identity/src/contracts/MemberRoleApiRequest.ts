/** Mirrors `MemberRoleApiRequest` of the be contract: gives a member another role. */
export interface MemberRoleApiRequest {
  /** The role, one of `OrganizationRoleConstants`, `owner` included. */
  readonly role: string;
}
