/** Mirrors `RegisterAccountApiRequest` of the be contract: registers an account with an email and password. */
export interface RegisterAccountApiRequest {
  /** The account email. */
  readonly email: string;
  /** The initial password. */
  readonly password: string;
  /** The login name; the email when omitted. */
  readonly userName?: string;
}
