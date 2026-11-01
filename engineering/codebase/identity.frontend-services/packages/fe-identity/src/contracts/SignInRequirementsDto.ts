/** Mirrors `SignInRequirementsDto` of the be contract: the verifications a sign-in needs before any token. */
export interface SignInRequirementsDto {
  /** Whether the email must be confirmed. */
  readonly isConfirmedEmailRequired: boolean;
  /** Whether the phone number must be confirmed. */
  readonly isConfirmedPhoneNumberRequired: boolean;
  /** Whether everyone needs a second factor. */
  readonly isSecondFactorRequired: boolean;
}
