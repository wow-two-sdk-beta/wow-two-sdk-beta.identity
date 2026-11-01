import type { SignInFieldDto } from './SignInFieldDto';
import type { SignInRuleDto } from './SignInRuleDto';

/** Mirrors `SignInMethodDto` of the be contract: one enabled sign-in method and the fields its form renders. */
export interface SignInMethodDto {
  /** The method, a `SignInMethodIdConstants` value; a client skips ids it does not know. */
  readonly id: string;
  /** The channels a code goes out over, such as `telegram`; empty for a method without codes. */
  readonly channels: ReadonlyArray<string>;
  /** The sign-in form's fields, in order. */
  readonly fields: ReadonlyArray<SignInFieldDto>;
  /**
   * The rules a new password meets on sign-up, reset and change; the password method only, `null` for any other. The
   * sign-in form's own password field only requires a value.
   */
  readonly passwordRules: ReadonlyArray<SignInRuleDto> | null;
}
