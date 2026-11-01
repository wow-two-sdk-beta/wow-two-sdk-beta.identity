/** Mirrors `SignUpApiRequest` of the be contract: signs an account up with an email and password. */
export interface SignUpApiRequest {
  /** The account email. */
  readonly email: string;
  /** The initial password. */
  readonly password: string;
  /** The user name; the email when omitted. */
  readonly userName?: string;
}
