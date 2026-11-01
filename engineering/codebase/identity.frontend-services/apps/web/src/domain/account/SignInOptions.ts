import type { SignInMethod } from './SignInMethod';

/** Represents how the hosted pages look and sign people in, for the instance or the app being served. */
export interface SignInOptions {
  /** The name the pages show: the instance owner's brand. */
  readonly brandName: string;
  /** The fe sdk theme the pages take, by its registry id (`platform`, `ocharo`). */
  readonly theme: string;
  /** A same-origin image behind the card, or none for the theme's own backdrop. */
  readonly backgroundImageUrl: string | null;
  /** Whether anyone may create an account. */
  readonly isSignUpEnabled: boolean;
  /** The enabled sign-in methods, in the order the sign-in page lays them out. */
  readonly methods: ReadonlyArray<SignInMethod>;
}
