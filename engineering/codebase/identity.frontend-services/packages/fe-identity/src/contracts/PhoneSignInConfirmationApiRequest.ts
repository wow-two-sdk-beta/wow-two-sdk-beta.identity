/** Mirrors `PhoneSignInConfirmationApiRequest` of the be contract: asks where a new device's confirmation stands. */
export interface PhoneSignInConfirmationApiRequest {
  /** The `token` of the device confirmation. */
  readonly token: string;
  /**
   * Whether the person asked to stay signed in ("Keep me signed in"): the session takes the instance's extended lifetime
   * and a cookie that outlives the browser; ignored where `SignInOptionsDto.isPersistenceOffered` is false.
   */
  readonly isPersistent?: boolean;
}
