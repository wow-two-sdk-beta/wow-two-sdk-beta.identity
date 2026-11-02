/** Names the instance roles the console reads: `admin` also creates organizations; `support` and `auditor` open the console. */
export const InstanceRole = {
  admin: 'admin',
  support: 'support',
  auditor: 'auditor',
} as const;

/**
 * Names the codes a refused organization creation carries in its problem's `identityCode`: `400` for an invalid name or
 * handle, `409` for a taken handle or id.
 */
export const ConsoleErrorCode = {
  organizationNameRequired: 'OrganizationNameRequired',
  invalidOrganizationSlug: 'InvalidOrganizationSlug',
  duplicateOrganizationSlug: 'DuplicateOrganizationSlug',
  duplicateOrganizationId: 'DuplicateOrganizationId',
} as const;

/** Represents an organization the console's person manages as an owner or admin. */
export interface ConsoleOrganization {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  /** `owner` or `admin`. */
  readonly role: string;
}

/**
 * Represents the person signed in to the console: one holding an instance role, or owning or administering an
 * organization.
 */
export interface ConsoleSession {
  readonly userId: string;
  /** The name the console shows: the display name, else the phone, else the user name. */
  readonly name: string;
  /** The instance roles the person's sign-in carries, in catalog order: `admin`, `support`, `auditor`; empty for an organization's owner or admin without one. */
  readonly roles: ReadonlyArray<string>;
  /** The organizations the person owns or administers, where the console makes invitations and manages members. */
  readonly organizations: ReadonlyArray<ConsoleOrganization>;
}

/** Represents an organization an instance admin creates in the console, becoming its owner. */
export interface NewConsoleOrganization {
  readonly name: string;
  /** The handle: lowercase letters, digits and single hyphens. */
  readonly slug: string;
  /** The id to keep, such as a product's existing key; omitted, the service assigns one. */
  readonly id?: string;
}

/** Whether the session's person holds the instance role `admin`, which creates organizations. */
export function isInstanceAdmin(session: ConsoleSession): boolean {
  return session.roles.includes(InstanceRole.admin);
}
