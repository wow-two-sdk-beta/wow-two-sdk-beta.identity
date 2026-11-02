/**
 * Mirrors `OrganizationRoleConstants` of the be contract: the fixed role catalog every instance carries; an app adds
 * permissions to these roles, never a role of its own.
 */
export const OrganizationRoleConstants = {
  /** Owns the organization: manages members and invitations, as an admin does. */
  owner: 'owner',
  /** Manages members and invitations, owners included, as an owner does. */
  admin: 'admin',
  /** Works on the organization's content. */
  editor: 'editor',
  /** Reads the organization's content. */
  viewer: 'viewer',
  /** Handles the organization's billing. */
  billing: 'billing',
} as const;

/** One role of the organization catalog. */
export type OrganizationRole = (typeof OrganizationRoleConstants)[keyof typeof OrganizationRoleConstants];

/** Every role in the catalog, owner first. */
export const organizationRoles: ReadonlyArray<OrganizationRole> = Object.values(OrganizationRoleConstants);
