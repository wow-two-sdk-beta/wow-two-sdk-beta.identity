import type { PhoneSignInTargetDto } from './PhoneSignInTargetDto';

/** Mirrors `PhoneSignInTargetsDto` of the be contract: where else the phone's sign-in code can go. */
export interface PhoneSignInTargetsDto {
  /** The phone's trusted contacts; empty when it has none, which the answer for an unknown number shares. */
  readonly trustedContacts: ReadonlyArray<PhoneSignInTargetDto>;
}
