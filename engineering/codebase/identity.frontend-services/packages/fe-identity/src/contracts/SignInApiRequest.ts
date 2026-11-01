/** Mirrors `SignInApiRequest` of the be contract: signs in with a login and password. */
export interface SignInApiRequest {
  /** The user name or email. */
  readonly login: string;
  /** The password. */
  readonly password: string;
}
