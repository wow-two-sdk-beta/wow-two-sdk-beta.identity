import type { InvitationStatus, OrganizationMemberDto } from '../../contracts';
import type { AccountText } from '../../kit/account/hooks';

const roleNames: Readonly<Record<string, string>> = {
  owner: 'Owner',
  admin: 'Admin',
  editor: 'Editor',
  viewer: 'Viewer',
  billing: 'Billing',
};

const statusNames: Readonly<Record<InvitationStatus, string>> = {
  pending: 'Pending',
  expired: 'Expired',
  accepted: 'Accepted',
  revoked: 'Revoked',
};

/** Names a role: the consumer's `IdentityKit.Roles.{role}` message, else its English name, else the role itself. */
export function roleLabel(t: AccountText, role: string): string {
  return t(`Roles.${role}`, roleNames[role] ?? role);
}

/** Names where an invitation stands: the consumer's `IdentityKit.InvitationStatus.{status}` message, else English. */
export function invitationStatusLabel(t: AccountText, status: InvitationStatus): string {
  return t(`InvitationStatus.${status}`, statusNames[status]);
}

/** Names a member: their display name, else user name, email, phone number or id. */
export function memberName(member: OrganizationMemberDto): string {
  return member.displayName || member.userName || member.email || member.phoneNumber || member.userId;
}
