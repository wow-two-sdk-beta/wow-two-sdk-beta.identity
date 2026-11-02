/** Mirrors `SignInApiRequest` of the be contract: signs in with an identifier and password. */
export interface SignInApiRequest {
  /** The user name or email. */
  readonly identifier: string;
  /** The password. */
  readonly password: string;
  /** A second-factor code, repeating the sign-in after it answered `TwoFactorRequired` without a ticket. */
  readonly twoFactorCode?: string;
  /** The second-factor method, such as `email`; the account's preferred one when omitted. */
  readonly twoFactorMethod?: string;
  /** A recovery code, instead of a second-factor code. */
  readonly recoveryCode?: string;
  /**
   * Whether the person asked to stay signed in ("Keep me signed in"): the session takes the instance's extended lifetime
   * and a cookie that outlives the browser; ignored where `SignInOptionsDto.isPersistenceOffered` is false.
   */
  readonly isPersistent?: boolean;
}
