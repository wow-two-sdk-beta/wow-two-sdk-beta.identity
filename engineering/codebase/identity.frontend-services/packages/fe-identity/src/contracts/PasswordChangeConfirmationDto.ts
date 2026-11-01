import type { PasswordChangeConfirmationType } from './PasswordChangeConfirmationType';

/** Mirrors `PasswordChangeConfirmationDto` of the be contract: how a change is confirmed, and whether a code or link went out. */
export interface PasswordChangeConfirmationDto {
  /** How the change is confirmed. */
  readonly confirmation: PasswordChangeConfirmationType;
  /** Whether a code or link was sent; always `false` under `None`. */
  readonly isSent: boolean;
}
