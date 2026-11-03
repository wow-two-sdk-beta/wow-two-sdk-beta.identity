/** Mirrors `InviteSignUpStartedDto` of the be contract: what starting an invitation sign-up answers. */
export interface InviteSignUpStartedDto {
  /**
   * The bot's start link: sharing the contact there proves the phone and links the chat. `null` when the code went to the
   * trusted contact instead.
   */
  readonly telegramLinkUrl: string | null;
  /** Whether a code went to the invitation's trusted contact, which `invitations/sign-up/verify` takes. */
  readonly isCodeSentToTrustedContact: boolean;
  /** When the attempt stops waiting, ISO 8601; empty where the answer gives none. */
  readonly expiresAt: string;
}
