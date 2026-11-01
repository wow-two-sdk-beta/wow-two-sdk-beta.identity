/** Mirrors `PhoneSignInSentDto` of the be contract: what a phone sign-in send answers. */
export interface PhoneSignInSentDto {
  /** Whether a code went out. */
  readonly isCodeSent: boolean;
  /** The bot's start link when the phone has no linked chat yet: the code follows once the person shares the contact. */
  readonly telegramLinkUrl: string | null;
}
