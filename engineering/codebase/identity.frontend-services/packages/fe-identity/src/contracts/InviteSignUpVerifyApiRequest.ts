/** Mirrors `InviteSignUpVerifyApiRequest` of the be contract: the code the trusted contact received for an invitation sign-up. */
export interface InviteSignUpVerifyApiRequest {
  /** The one-time code the invitation's trusted contact received in Telegram. */
  readonly code: string;
  /**
   * Whether the person asked to stay signed in ("Keep me signed in"): the session takes the instance's extended lifetime
   * and a cookie that outlives the browser; ignored where `SignInOptionsDto.isPersistenceOffered` is false.
   */
  readonly isPersistent?: boolean;
}
