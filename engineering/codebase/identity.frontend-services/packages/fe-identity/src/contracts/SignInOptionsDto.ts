import type { SignInMethodDto } from './SignInMethodDto';
import type { SignInRequirementsDto } from './SignInRequirementsDto';

/**
 * Mirrors `SignInOptionsDto` of the be contract, the sign-in declaration `GET sign-in-options` answers: the hosted pages'
 * look, whether anyone may sign up, and each enabled method in layout order with its fields and rules.
 */
export interface SignInOptionsDto {
  /** The name the pages show, the instance owner's brand. */
  readonly brandName: string;
  /** The fe sdk theme by its registry id: the app's own, else the instance's. */
  readonly theme: string;
  /** The image behind the card, a same-origin path or an HTTPS URL; `null` for the theme's own backdrop. */
  readonly backgroundUrl: string | null;
  /** Whether anyone may create an account. */
  readonly isSignUpEnabled: boolean;
  /** The locale the rule messages are in: `uz`, `ru` or `en`. */
  readonly locale: string;
  /** The locales the pages offer, in display order. */
  readonly locales: ReadonlyArray<string>;
  /** The enabled methods, in the order the pages lay them out. */
  readonly methods: ReadonlyArray<SignInMethodDto>;
  /** The verifications a sign-in needs. */
  readonly requirements: SignInRequirementsDto;
}
