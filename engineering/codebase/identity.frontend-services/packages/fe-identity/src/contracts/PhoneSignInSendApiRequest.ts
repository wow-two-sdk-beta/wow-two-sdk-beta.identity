/** Mirrors `PhoneSignInSendApiRequest` of the be contract: the phone a code goes to. */
export interface PhoneSignInSendApiRequest {
  /** The phone in E.164 form. */
  readonly phone: string;
  /**
   * The `id` of one of `PhoneSignInTargetsDto.trustedContacts`: the code then goes to that contact's own Telegram instead
   * of the phone's; omitted, it goes to the phone's own.
   */
  readonly targetId?: string;
}
