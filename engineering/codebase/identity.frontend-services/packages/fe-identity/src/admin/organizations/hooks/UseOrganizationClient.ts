import { createOrganizationClient, type OrganizationClient } from '../CreateOrganizationClient';

/** Names where an admin panel reaches the organization API. */
export interface OrganizationClientSource {
  /** The client to call; built from `baseUrl` when omitted. */
  readonly client?: OrganizationClient;
  /** The service's origin; the consumer's own origin (Form 1) when omitted. */
  readonly baseUrl?: string;
}

/** Returns the panel's organization client, read once at setup: the given one, or one for `baseUrl`. */
export function useOrganizationClient(source: OrganizationClientSource): OrganizationClient {
  return source.client ?? createOrganizationClient(source.baseUrl ? { baseUrl: source.baseUrl } : {});
}
