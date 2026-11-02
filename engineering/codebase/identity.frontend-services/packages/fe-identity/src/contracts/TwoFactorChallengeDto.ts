/**
 * Mirrors the `401` problem a sign-in answers when the account needs a second factor (`identityCode`
 * `TwoFactorRequired`): the method it asks for and, when the first factor cannot be repeated, such as a spent phone
 * code, the ticket `sign-in/two-factor` takes.
 */
export interface TwoFactorChallengeDto {
  /** The second-factor method asked for, such as `authenticator` or `email`; `null` when the problem names none. */
  readonly twoFactorMethod: string | null;
  /** The account the ticket belongs to; `null` when no ticket came. */
  readonly userId: string | null;
  /** The ticket `sign-in/two-factor` completes; `null` when the first factor is repeated with the code instead. */
  readonly twoFactorTicket: string | null;
}
