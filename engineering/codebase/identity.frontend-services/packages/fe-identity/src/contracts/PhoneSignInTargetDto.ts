/** Mirrors the be contract's code destination: one trusted contact a phone's sign-in code can go to instead. */
export interface PhoneSignInTargetDto {
  /** The opaque id `PhoneSignInSendApiRequest.targetId` names. */
  readonly id: string;
  /** The contact's masked label, such as `L*** K.`. */
  readonly label: string;
}
