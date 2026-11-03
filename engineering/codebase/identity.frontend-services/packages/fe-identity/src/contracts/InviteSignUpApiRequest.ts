import type { InviteSignUpVerification } from './InviteSignUpVerification';

/**
 * Mirrors the `multipart/form-data` body of `POST invitations/{token}/sign-up`: the profile an invitation sign-up gives
 * and how its phone is verified. Each part is sent only when given.
 */
export interface InviteSignUpApiRequest {
  /** The name the person goes by, the `displayName` text part. */
  readonly displayName?: string | null;
  /** The profile picture, the `picture` file part: JPEG, PNG or WebP within the declared `pictureRules`. */
  readonly picture?: Blob | null;
  /**
   * The `verification` text part: `telegram`, the default, for the bot's start link and a shared contact, or
   * `trusted-contact` for a code sent to the invitation's trusted contact.
   */
  readonly verification?: InviteSignUpVerification | null;
}
