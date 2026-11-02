/** Mirrors `OrganizationMemberDto` of the be contract: one member of an organization, as its owners and admins see them. */
export interface OrganizationMemberDto {
  /** The person's id, the `sub` of their tokens. */
  readonly userId: string;
  /** The name the person goes by; `null` when they set none. */
  readonly displayName: string | null;
  /** The person's user name. */
  readonly userName: string | null;
  /** The person's email; `null` without one. */
  readonly email: string | null;
  /** The person's phone number in E.164; `null` without one. */
  readonly phoneNumber: string | null;
  /** The person's role in the organization, one of `OrganizationRoleConstants`. */
  readonly role: string;
  /** When the person joined, ISO 8601. */
  readonly joinedAt: string;
  /**
   * Whether another Telegram account shared the person's confirmed phone number within the last day and waits for an
   * owner or admin to approve or deny moving the number's link to it; `false` while the instance has no bot.
   */
  readonly hasPendingTelegramChange: boolean;
}
