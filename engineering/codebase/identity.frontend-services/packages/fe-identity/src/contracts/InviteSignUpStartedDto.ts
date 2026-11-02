/** Mirrors `InviteSignUpStartedDto` of the be contract: what starting an invitation sign-up answers. */
export interface InviteSignUpStartedDto {
  /** The bot's start link: sharing the contact there proves the phone and links the chat. */
  readonly telegramLinkUrl: string;
  /** When the attempt stops waiting for the contact, ISO 8601. */
  readonly expiresAt: string;
}
