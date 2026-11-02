import type { ProfileFieldMode, ProfilePictureRulesDto } from '@wow-two-beta/fe-identity/contracts';

/** Represents what an invitation sign-up asks for beside the phone the Telegram bot verifies. */
export interface SignUpProfile {
  /** Whether the sign-up asks for the name the person goes by: `off`, `optional` or `required`. */
  readonly displayName: ProfileFieldMode;
  /** Whether the sign-up asks for a profile picture: `off`, `optional` or `required`. */
  readonly picture: ProfileFieldMode;
  /** What a picture upload accepts: its types, largest size and shortest and longest side. */
  readonly pictureRules: ProfilePictureRulesDto;
}
