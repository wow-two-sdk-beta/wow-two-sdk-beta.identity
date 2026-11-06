/**
 * Mirrors the be contract's `deviceConfirmation` of `POST phone-sign-in` and `phone-sign-in/choose`: the browser is new to
 * the account, so a trusted contact's press in Telegram comes before the session. No session is set until
 * `POST phone-sign-in/confirmation` answers `204`.
 */
export interface DeviceConfirmationDto {
  /** The token that `phone-sign-in/confirmation` polls with. */
  readonly token: string;
  /** When the request ends, as an ISO 8601 instant, by the service's clock; the poll's `401` ends a wait, not this. */
  readonly expiresAt: string;
  /** The masked labels of the contacts who were asked. */
  readonly contacts: ReadonlyArray<string>;
}
