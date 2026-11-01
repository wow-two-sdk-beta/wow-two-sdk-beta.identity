/** Mirrors `PhoneSignInApiRequest` of the be contract: the phone and the code sent to it. */
export interface PhoneSignInApiRequest {
  /** The phone in E.164 form. */
  readonly phone: string;
  /** The one-time code. */
  readonly code: string;
}
