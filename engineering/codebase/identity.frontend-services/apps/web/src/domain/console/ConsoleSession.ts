/** Represents the person signed in to the console and the instance roles that open its areas. */
export interface ConsoleSession {
  readonly userId: string;
  /** The name the console shows: the display name, else the phone, else the user name. */
  readonly name: string;
  /** The instance roles the person holds, in catalog order: `admin`, `support`, `auditor`. */
  readonly roles: ReadonlyArray<string>;
}
