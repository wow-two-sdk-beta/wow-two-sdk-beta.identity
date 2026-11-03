/** Mirrors `PhoneSignInChooseApiRequest` of the be contract: the account a trusted contact signs in as. */
export interface PhoneSignInChooseApiRequest {
  /** The `token` of the account choice. */
  readonly token: string;
  /** The picked account. */
  readonly userId: string;
  /**
   * Whether the person asked to stay signed in ("Keep me signed in"): the session takes the instance's extended lifetime
   * and a cookie that outlives the browser; ignored where `SignInOptionsDto.isPersistenceOffered` is false.
   */
  readonly isPersistent?: boolean;
}
