import { createAccountClient, type AccountClient } from '../CreateAccountClient';

/** Names where a kit component reaches the account API. */
export interface AccountClientSource {
  /** The client to call; built from `baseUrl` when omitted. */
  readonly client?: AccountClient;
  /** The service's origin; the consumer's own origin (Form 1) when omitted. */
  readonly baseUrl?: string;
}

/** Returns the component's account client, read once at setup: the given one, or one for `baseUrl`. */
export function useAccountClient(source: AccountClientSource): AccountClient {
  return source.client ?? createAccountClient(source.baseUrl ? { baseUrl: source.baseUrl } : {});
}
