/** Represents an organization the console's person manages as an owner or admin. */
export interface ConsoleOrganization {
  readonly id: string;
  readonly name: string;
  readonly slug: string;
  /** `owner` or `admin`. */
  readonly role: string;
}

/** Represents the person signed in to the console, the instance roles that open its areas and the organizations they manage. */
export interface ConsoleSession {
  readonly userId: string;
  /** The name the console shows: the display name, else the phone, else the user name. */
  readonly name: string;
  /** The instance roles the person holds, in catalog order: `admin`, `support`, `auditor`. */
  readonly roles: ReadonlyArray<string>;
  /** The organizations the person owns or administers, where the console makes invitations and manages members. */
  readonly organizations: ReadonlyArray<ConsoleOrganization>;
}
