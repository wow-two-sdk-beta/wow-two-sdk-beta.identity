/**
 * Mirrors the organization management API's routes, relative to the service or consumer origin. Each route sits below
 * `/{organizationId}` and answers the organization's owners and admins, signed in by cookie.
 */
export const OrganizationRouteConstants = {
  /** The organization API's mount point. */
  base: '/api/organizations',
  /** `GET`: the members; `PUT /members/{userId}` with `{ role }` re-roles one; `DELETE /members/{userId}` removes one. */
  members: '/members',
  /**
   * `POST` with `{ role, phoneNumber?, email? }`: makes an invitation, its link in that answer alone; `GET`: the
   * invitations, newest first; `DELETE /invitations/{invitationId}` revokes one.
   */
  invitations: '/invitations',
} as const;
