/** Mirrors `PhoneTelegramLinkDto` of the be contract: the bot link that adds or changes the signed-in account's phone. */
export interface PhoneTelegramLinkDto {
  /** The bot's `add-phone` start link: sharing the contact there confirms the number and links the chat. */
  readonly telegramLinkUrl: string;
}
