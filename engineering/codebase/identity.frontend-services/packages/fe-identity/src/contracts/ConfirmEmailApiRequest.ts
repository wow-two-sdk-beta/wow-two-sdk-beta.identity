/** Mirrors the `confirm-email` route's query: the account and the token its confirmation link carries. */
export interface ConfirmEmailApiRequest {
  /** The account id. */
  readonly userId: string;
  /** The emailed confirmation token. */
  readonly token: string;
}
