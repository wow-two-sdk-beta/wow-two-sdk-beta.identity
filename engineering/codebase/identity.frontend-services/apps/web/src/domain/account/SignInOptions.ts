import type { SignInRuleDto, SignUpMode } from '@wow-two-beta/fe-identity/contracts';
import type { SignInMethod } from './SignInMethod';
import type { SignUpProfile } from './SignUpProfile';

/** Represents how the hosted pages look and sign people in, for the instance, one look for every client. */
export interface SignInOptions {
  /** The name the pages show: the instance owner's brand. */
  readonly brandName: string;
  /** The fe sdk theme the pages take, by its registry id (`platform`, `ocharo`). */
  readonly theme: string;
  /** The image behind the card, a same-origin path or an HTTPS URL, or none for the theme's own backdrop. */
  readonly backgroundImageUrl: string | null;
  /** Who may create an account: `invite`, only through an invitation link; `open`, anyone. */
  readonly signUpMode: SignUpMode;
  /** Whether the sign-in forms offer "Keep me signed in". */
  readonly isPersistenceOffered: boolean;
  /** Whether the instance takes emails: sign-up by email, confirmation and the forgot-password link. */
  readonly isEmailEnabled: boolean;
  /** Whether trusted contacts are on: the phone sign-in offers a phone's contacts, and the security page lists them. */
  readonly isTrustedContactSignInEnabled: boolean;
  /** The enabled sign-in methods, in the order the sign-in page lays them out. */
  readonly methods: ReadonlyArray<SignInMethod>;
  /** The rules a new password meets on sign-up, reset and change, in the request's locale; empty without a password method. */
  readonly passwordRules: ReadonlyArray<SignInRuleDto>;
  /** What an invitation sign-up by phone asks for beside the phone: a name and a picture, each off, optional or required. */
  readonly signUpProfile: SignUpProfile;
}
