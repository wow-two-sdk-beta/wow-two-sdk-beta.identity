/**
 * Mirrors the `multipart/form-data` body of `POST invitations/{token}/sign-up`: the profile an invitation sign-up gives
 * beside the phone the Telegram bot verifies. Each part is sent only when given.
 */
export interface InviteSignUpApiRequest {
  /** The name the person goes by, the `displayName` text part. */
  readonly displayName?: string | null;
  /** The profile picture, the `picture` file part: JPEG, PNG or WebP within the declared `pictureRules`. */
  readonly picture?: Blob | null;
}
