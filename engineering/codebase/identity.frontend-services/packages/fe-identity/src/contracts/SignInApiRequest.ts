/** Mirrors `SignInApiRequest` of the be contract: signs in with an identifier and password. */
export interface SignInApiRequest {
  /** The user name or email. */
  readonly identifier: string;
  /** The password. */
  readonly password: string;
}
