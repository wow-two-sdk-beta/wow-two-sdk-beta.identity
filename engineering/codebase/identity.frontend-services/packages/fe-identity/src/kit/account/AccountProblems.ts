import { ProblemDetailsExtensions } from '@wow-two-beta/fe/foundation/http';
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { IdentityErrorCodeConstants, type TwoFactorChallengeDto } from '../../contracts';

function filledText(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null;
}

/** Reads the `identityCode` a refused account call's problem carries; `null` without one. */
export function readIdentityCode(failure: AppError | null | undefined): string | null {
  return filledText(ProblemDetailsExtensions.of(failure)?.identityCode);
}

/**
 * Reads the second-factor challenge of a refused sign-in: a `401` problem that says `TwoFactorRequired`, or names the
 * method or ticket a second factor takes; `null` for any other failure. The ticket counts only beside its `userId`.
 */
export function readTwoFactorChallenge(failure: AppError | null | undefined): TwoFactorChallengeDto | null {
  const problem = ProblemDetailsExtensions.of(failure);
  if (problem?.status !== 401) return null;
  const twoFactorMethod = filledText(problem.twoFactorMethod);
  const userId = filledText(problem.userId);
  const twoFactorTicket = filledText(problem.twoFactorTicket);
  const isChallenge =
    filledText(problem.identityCode) === IdentityErrorCodeConstants.twoFactorRequired ||
    twoFactorMethod !== null ||
    twoFactorTicket !== null;
  if (!isChallenge) return null;
  return userId && twoFactorTicket
    ? { twoFactorMethod, userId, twoFactorTicket }
    : { twoFactorMethod, userId: null, twoFactorTicket: null };
}
