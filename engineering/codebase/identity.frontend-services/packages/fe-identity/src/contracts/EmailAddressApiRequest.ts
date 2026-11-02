/** Mirrors `EmailAddressApiRequest` of the be contract: names the account an email is sent to. */
export interface EmailAddressApiRequest {
  /** The account email. */
  readonly email: string;
  /** The local path the emailed link continues to once the person signs in; the service drops anything else. */
  readonly returnUrl?: string;
}
