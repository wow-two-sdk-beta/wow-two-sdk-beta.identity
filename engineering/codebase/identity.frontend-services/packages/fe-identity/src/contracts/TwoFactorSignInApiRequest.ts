/** Mirrors `TwoFactorSignInApiRequest` of the be contract: completes a sign-in whose first factor answered with a ticket. */
export interface TwoFactorSignInApiRequest {
  /** The `userId` of the challenge. */
  readonly userId: string;
  /** The `twoFactorTicket` of the challenge. */
  readonly ticket: string;
  /** The second-factor code, from the authenticator or the delivered method. */
  readonly twoFactorCode?: string;
  /** The method, such as `email`; the authenticator when omitted. */
  readonly twoFactorMethod?: string;
  /** A recovery code, instead of a second-factor code. */
  readonly recoveryCode?: string;
  /**
   * Whether the person asked to stay signed in ("Keep me signed in"), as the first factor's request said; ignored where
   * `SignInOptionsDto.isPersistenceOffered` is false.
   */
  readonly isPersistent?: boolean;
}
