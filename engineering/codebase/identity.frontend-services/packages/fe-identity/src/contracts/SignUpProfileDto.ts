import type { ProfileFieldMode } from './ProfileFieldMode';
import type { ProfilePictureRulesDto } from './ProfilePictureRulesDto';

/**
 * Mirrors `SignUpProfileDto` of the be contract: the profile fields an invitation sign-up asks for beside the phone the
 * Telegram bot verifies (`Identity:SignUp:Profile`), each `off` unless the instance asks for it.
 */
export interface SignUpProfileDto {
  /** Whether the sign-up asks for the name the person goes by. */
  readonly displayName: ProfileFieldMode;
  /** Whether the sign-up asks for a profile picture. */
  readonly picture: ProfileFieldMode;
  /** What a picture upload accepts. */
  readonly pictureRules: ProfilePictureRulesDto;
}
