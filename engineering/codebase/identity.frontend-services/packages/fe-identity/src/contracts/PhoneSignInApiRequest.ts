/** Mirrors `PhoneSignInApiRequest` of the be contract: the phone and the code sent to it. */
export interface PhoneSignInApiRequest {
  /** The phone in E.164 form. */
  readonly phone: string;
  /** The one-time code. */
  readonly code: string;
  /**
   * Whether the person asked to stay signed in ("Keep me signed in"): the session takes the instance's extended lifetime
   * and a cookie that outlives the browser; ignored where `SignInOptionsDto.isPersistenceOffered` is false.
   */
  readonly isPersistent?: boolean;
  /**
   * The token of an invitation link: the person joins its organization with its role, and a first sign-in creates them
   * even while sign-up is by invitation only. An invitation naming a phone number takes only that number.
   */
  readonly invitationToken?: string;
}
