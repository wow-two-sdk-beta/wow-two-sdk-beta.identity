/** Mirrors `SignUpApiRequest` of the be contract: signs an account up with an email and password. */
export interface SignUpApiRequest {
  /** The account email. */
  readonly email: string;
  /** The initial password. */
  readonly password: string;
  /** The user name; the email when omitted. */
  readonly userName?: string;
  /**
   * The token of an invitation link: the account joins its organization with its role, and signs up even while sign-up
   * is by invitation only. An invitation naming an email takes only that email.
   */
  readonly invitationToken?: string;
  /** The local path the confirmation link continues to once the person signs in; the service drops anything else. */
  readonly returnUrl?: string;
}
