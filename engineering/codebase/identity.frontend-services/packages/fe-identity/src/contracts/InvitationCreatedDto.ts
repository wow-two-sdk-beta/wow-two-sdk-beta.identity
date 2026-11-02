import type { InvitationDto } from './InvitationDto';

/** Mirrors `InvitationCreatedDto` of the be contract: a new invitation with its link, which this answer alone carries. */
export interface InvitationCreatedDto {
  /** The invitation. */
  readonly invitation: InvitationDto;
  /** The link's token, shown once. */
  readonly token: string;
  /** The link to send, on the hosted sign-up page; `null` when the instance names no origin. */
  readonly link: string | null;
}
