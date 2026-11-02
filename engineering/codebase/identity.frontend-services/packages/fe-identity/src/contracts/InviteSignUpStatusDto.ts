import type { InviteSignUpStatus } from './InviteSignUpStatus';

/** Mirrors `InviteSignUpStatusDto` of the be contract: one poll of the invitation sign-up this browser started. */
export interface InviteSignUpStatusDto {
  /** Where the attempt stands. */
  readonly status: InviteSignUpStatus;
  /**
   * Why a `failed` attempt was refused, an identity code: `InvitationExpired`, `InvitationAccepted`, `InvitationRevoked`,
   * `InvitationIdentifierMismatch` or `PhoneCountryNotAllowed`; `null` otherwise.
   */
  readonly failureCode: string | null;
  /**
   * Whether this answer set the session cookie: `true` on the first `completed` poll; `false` when the person exists
   * and signs in the usual way, such as with a second factor.
   */
  readonly isSignedIn: boolean;
}
