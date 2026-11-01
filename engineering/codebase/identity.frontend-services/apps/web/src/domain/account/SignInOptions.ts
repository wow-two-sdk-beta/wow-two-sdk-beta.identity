import type { SignInRuleDto } from '@wow-two-beta/fe-identity/contracts';
import type { SignInMethod } from './SignInMethod';

/** Represents how the hosted pages look and sign people in, for the instance, one look for every client. */
export interface SignInOptions {
  /** The name the pages show: the instance owner's brand. */
  readonly brandName: string;
  /** The fe sdk theme the pages take, by its registry id (`platform`, `ocharo`). */
  readonly theme: string;
  /** The image behind the card, a same-origin path or an HTTPS URL, or none for the theme's own backdrop. */
  readonly backgroundImageUrl: string | null;
  /** Whether anyone may create an account. */
  readonly isSignUpEnabled: boolean;
  /** The enabled sign-in methods, in the order the sign-in page lays them out. */
  readonly methods: ReadonlyArray<SignInMethod>;
  /** The rules a new password meets on sign-up, reset and change, in the request's locale; empty without a password method. */
  readonly passwordRules: ReadonlyArray<SignInRuleDto>;
}
