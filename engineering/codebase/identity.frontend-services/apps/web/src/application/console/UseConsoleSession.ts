import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { computed, onMounted, onScopeDispose, shallowRef } from 'vue';
import type { ConsoleSession, NewConsoleOrganization } from '../../domain/console/ConsoleSession';
import { accountApi } from '../../integration/account';
import { consoleApi } from '../../integration/console';

/** Holds what the console does when the session ends or is missing. */
export interface ConsoleSessionHandlers {
  /** Runs when nobody is signed in: leaves for the sign-in page, which comes back. */
  readonly onSignInRequired: () => void;
  /** Runs once the person signed out: leaves for the sign-in page. */
  readonly onSignedOut: () => void;
}

/**
 * Loads the console's session for this component's lifetime: `isLoading` holds until it resolves, `onSignInRequired`
 * runs when nobody is signed in, and `isDenied` turns on for a person with neither an instance role nor an organization
 * they own or administer. The service reads roles and organizations from its store on every request, so `refresh` shows a
 * grant or revoke made since, keeping the current session on screen until the answer arrives. `createOrganization`
 * makes an instance admin's organization and lists it in the session; `signOut` ends the cookie session and runs
 * `onSignedOut`.
 */
export function useConsoleSession(handlers: ConsoleSessionHandlers) {
  const session = shallowRef<ConsoleSession | null>(null);
  const isDenied = shallowRef(false);
  const failure = shallowRef<ApiFailure | null>(null);
  const isLoading = computed(() => session.value === null && !isDenied.value && failure.value === null);
  const isSigningOut = shallowRef(false);
  const signOutFailure = shallowRef<ApiFailure | null>(null);
  let controller: AbortController | null = null;

  /** Reads the session again; a failure other than signed out or denied keeps the one on screen. */
  async function refresh() {
    controller?.abort();
    const current = new AbortController();
    controller = current;
    const result = await consoleApi.getSession(current.signal);
    if (current.signal.aborted) return;
    if (result.ok) {
      session.value = result.value;
      isDenied.value = false;
      failure.value = null;
    } else if (result.failure.status === 401) handlers.onSignInRequired();
    else if (result.failure.status === 403) {
      session.value = null;
      isDenied.value = true;
    } else if (session.value === null) failure.value = result.failure;
  }

  onMounted(() => void refresh());
  onScopeDispose(() => controller?.abort());

  async function createOrganization(request: NewConsoleOrganization, signal?: AbortSignal) {
    const result = await consoleApi.createOrganization(request, signal);
    if (result.ok && session.value)
      session.value = { ...session.value, organizations: [...session.value.organizations, result.value] };
    return result;
  }

  /** Signs out; already signed out counts as done. Any other failure stays on screen. */
  async function signOut() {
    if (isSigningOut.value) return;
    isSigningOut.value = true;
    signOutFailure.value = null;
    const result = await accountApi.signOut();
    isSigningOut.value = false;
    if (result.ok || result.failure.status === 401) handlers.onSignedOut();
    else signOutFailure.value = result.failure;
  }

  return { session, isLoading, isDenied, failure, refresh, createOrganization, signOut, isSigningOut, signOutFailure };
}
