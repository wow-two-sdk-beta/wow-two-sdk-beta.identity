/** Mirrors `SessionDto` of the be contract: one of the signed-in account's live sign-in sessions. */
export interface SessionDto {
  /** The session id, the `sid` claim. */
  readonly id: string;
  /** When it signed in, ISO 8601. */
  readonly createdAt: string;
  /** When it was last used, to within a few minutes, ISO 8601. */
  readonly lastSeenAt: string;
  /** When it ends on its own, ISO 8601. */
  readonly expiresAt: string;
  /** How it signed in, as `amr` values: `pwd`, `hwk`, `otp`, `mfa`. */
  readonly authenticationMethods: ReadonlyArray<string>;
  /** The network it signed in from, masked; `null` when unknown. */
  readonly ipMasked: string | null;
  /** The browser's user agent at sign-in; `null` when unknown. */
  readonly userAgent: string | null;
  /** Whether this is the session of the request asking. */
  readonly isCurrent: boolean;
  /** The name of the trusted contact whose code started it; `null` for a session signed in with the account's own means. */
  readonly trustedContactDisplayName: string | null;
}
