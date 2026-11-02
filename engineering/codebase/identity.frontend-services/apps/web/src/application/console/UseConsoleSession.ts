import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { computed, onMounted, onScopeDispose, shallowRef } from 'vue';
import type { ConsoleSession, NewConsoleOrganization } from '../../domain/console/ConsoleSession';
import { consoleApi } from '../../integration/console';

/**
 * Loads the console's session for this component's lifetime: `isLoading` holds until it resolves, `onSignInRequired`
 * runs when nobody is signed in, and `isDenied` turns on for a person with neither an instance role nor an organization
 * they own or administer. `createOrganization` makes an instance admin's organization and lists it in the session.
 */
export function useConsoleSession(onSignInRequired: () => void) {
  const controller = new AbortController();
  const session = shallowRef<ConsoleSession | null>(null);
  const isDenied = shallowRef(false);
  const failure = shallowRef<ApiFailure | null>(null);
  const isLoading = computed(() => session.value === null && !isDenied.value && failure.value === null);

  onMounted(async () => {
    const result = await consoleApi.getSession(controller.signal);
    if (controller.signal.aborted) return;
    if (result.ok) session.value = result.value;
    else if (result.failure.status === 401) onSignInRequired();
    else if (result.failure.status === 403) isDenied.value = true;
    else failure.value = result.failure;
  });
  onScopeDispose(() => controller.abort());

  async function createOrganization(request: NewConsoleOrganization, signal?: AbortSignal) {
    const result = await consoleApi.createOrganization(request, signal);
    if (result.ok && session.value)
      session.value = { ...session.value, organizations: [...session.value.organizations, result.value] };
    return result;
  }

  return { session, isLoading, isDenied, failure, createOrganization };
}
