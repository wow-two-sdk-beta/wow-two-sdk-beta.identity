import { ApiFailureFactory, createApiClient, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import {
  OrganizationRouteConstants,
  type InvitationApiRequest,
  type InvitationCreatedDto,
  type InvitationDto,
  type InvitationStatus,
  type MemberRoleApiRequest,
  type OrganizationMemberDto,
} from '../../contracts';

/** Configures where the organization API is reached. */
export interface OrganizationClientOptions {
  /** The origin serving the API: empty for the consumer's own backend (Form 1), the service's URL otherwise. */
  readonly baseUrl?: string;
  /**
   * Whether the browser sends the session cookie. Default `same-origin`, or `include` when `baseUrl` is set, which
   * the service's CORS policy must allow.
   */
  readonly credentials?: RequestCredentials;
  /** Replaces the global fetch, for tests. */
  readonly fetch?: typeof globalThis.fetch;
}

/**
 * Calls the organization management API over HTTP for the signed-in owner or admin. A person outside the organization
 * gets `404` `OrganizationNotFound`, a member who manages nothing `403` `OrganizationManagerRequired`; only an owner
 * invites, promotes, demotes or removes an owner (`403` `OrganizationOwnerRequired`), and the last owner stays
 * (`409` `LastOrganizationOwner`). The code rides in the problem's `identityCode` (`readIdentityCode`).
 */
export interface OrganizationClient {
  /** Reads the organization's members with their roles. */
  listMembers(
    organizationId: string,
    signal?: AbortSignal,
  ): Promise<Result<ReadonlyArray<OrganizationMemberDto>, ApiFailure>>;
  /** Gives a member another role, which ends their sessions. */
  changeMemberRole(
    organizationId: string,
    userId: string,
    request: MemberRoleApiRequest,
    signal?: AbortSignal,
  ): Promise<Result<void, ApiFailure>>;
  /** Removes a member from the organization, which ends their sessions. */
  removeMember(organizationId: string, userId: string, signal?: AbortSignal): Promise<Result<void, ApiFailure>>;
  /** Makes an invitation link; the answer alone carries its token and link. */
  createInvitation(
    organizationId: string,
    request: InvitationApiRequest,
    signal?: AbortSignal,
  ): Promise<Result<InvitationCreatedDto, ApiFailure>>;
  /** Reads the organization's invitations, newest first, without their tokens. */
  listInvitations(
    organizationId: string,
    signal?: AbortSignal,
  ): Promise<Result<ReadonlyArray<InvitationDto>, ApiFailure>>;
  /** Withdraws an unused invitation; `404` `InvitationNotFound` once it was used or withdrawn. */
  revokeInvitation(
    organizationId: string,
    invitationId: string,
    signal?: AbortSignal,
  ): Promise<Result<void, ApiFailure>>;
}

type Decoded<T> = Result<T, ApiFailure>;

const protocolFailure = () => ResultExtensions.fail(ApiFailureFactory.create('protocol'));

function record(value: unknown): Readonly<Record<string, unknown>> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function text(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

/** Decodes each item of a list, or `null` when it is no list or any item fails. */
function list<T>(value: unknown, decode: (item: unknown) => T | null): ReadonlyArray<T> | null {
  if (!Array.isArray(value)) return null;
  const items = value.map(decode);
  return items.every((item): item is T => item !== null) ? items : null;
}

function decodeMember(value: unknown): OrganizationMemberDto | null {
  const body = record(value);
  const userId = text(body?.userId);
  const role = text(body?.role);
  if (!body || !userId || !role) return null;
  return {
    userId,
    displayName: text(body.displayName),
    userName: text(body.userName),
    email: text(body.email),
    phoneNumber: text(body.phoneNumber),
    role,
    joinedAt: text(body.joinedAt) ?? '',
  };
}

const invitationStatuses: ReadonlyArray<InvitationStatus> = ['pending', 'expired', 'accepted', 'revoked'];

function decodeInvitation(value: unknown): InvitationDto | null {
  const body = record(value);
  const id = text(body?.id);
  const role = text(body?.role);
  const status = invitationStatuses.find((known) => known === body?.status);
  if (!body || !id || !role || !status) return null;
  return {
    id,
    role,
    status,
    phoneNumber: text(body.phoneNumber),
    email: text(body.email),
    createdAt: text(body.createdAt) ?? '',
    expiresAt: text(body.expiresAt) ?? '',
    acceptedAt: text(body.acceptedAt),
    acceptedByUserId: text(body.acceptedByUserId),
  };
}

function decodeMembers(value: unknown): Decoded<ReadonlyArray<OrganizationMemberDto>> {
  const members = list(value, decodeMember);
  return members ? ResultExtensions.ok(members) : protocolFailure();
}

function decodeInvitations(value: unknown): Decoded<ReadonlyArray<InvitationDto>> {
  const invitations = list(value, decodeInvitation);
  return invitations ? ResultExtensions.ok(invitations) : protocolFailure();
}

function decodeCreated(value: unknown): Decoded<InvitationCreatedDto> {
  const body = record(value);
  const invitation = decodeInvitation(body?.invitation);
  const token = text(body?.token);
  if (!body || !invitation || !token) return protocolFailure();
  return ResultExtensions.ok({ invitation, token, link: text(body.link) || null });
}

/** Creates a client for the organization API at `baseUrl` (same origin by default). */
export function createOrganizationClient(options: OrganizationClientOptions = {}): OrganizationClient {
  const baseUrl = options.baseUrl?.replace(/\/+$/, '') ?? '';
  const credentials = options.credentials ?? (baseUrl ? 'include' : undefined);
  const client = createApiClient({
    baseUrl: `${baseUrl}${OrganizationRouteConstants.base}`,
    ...(credentials !== undefined ? { credentials } : {}),
    ...(options.fetch !== undefined ? { fetch: options.fetch } : {}),
  });
  const members = (organizationId: string, userId?: string) =>
    `/${encodeURIComponent(organizationId)}${OrganizationRouteConstants.members}${userId ? `/${encodeURIComponent(userId)}` : ''}`;
  const invitations = (organizationId: string, invitationId?: string) =>
    `/${encodeURIComponent(organizationId)}${OrganizationRouteConstants.invitations}${invitationId ? `/${encodeURIComponent(invitationId)}` : ''}`;

  return {
    listMembers: (organizationId, signal) => client.get(members(organizationId), { signal, decode: decodeMembers }),
    changeMemberRole: (organizationId, userId, request, signal) =>
      client.put(members(organizationId, userId), { body: request, response: 'empty', signal }),
    removeMember: (organizationId, userId, signal) =>
      client.delete(members(organizationId, userId), { response: 'empty', signal }),
    createInvitation: (organizationId, request, signal) =>
      client.post(invitations(organizationId), { body: request, signal, decode: decodeCreated }),
    listInvitations: (organizationId, signal) =>
      client.get(invitations(organizationId), { signal, decode: decodeInvitations }),
    revokeInvitation: (organizationId, invitationId, signal) =>
      client.delete(invitations(organizationId, invitationId), { response: 'empty', signal }),
  };
}
