import type { PasswordChangeConfirmationType } from './PasswordChangeConfirmationType';

/**
 * Mirrors `PasswordChangeRequiredDto` of the be contract: a sign-in whose password matched but breaks the current
 * rules. It carries no session, only the ticket that opens the change routes.
 */
export interface PasswordChangeRequiredDto {
  /** Always `true`; tells this answer from a signed-in one. */
  readonly isPasswordChangeRequired: true;
  /** How the change is confirmed. */
  readonly confirmation: PasswordChangeConfirmationType;
  /** The ticket the change routes take; a change spends it. */
  readonly ticket: string;
}
