import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { onMounted, onScopeDispose, shallowRef } from 'vue';
import type { ConsoleSession } from '../../domain/console/ConsoleSession';
import { consoleApi } from '../../integration/console';

/**
 * Loads the console's session for this component's lifetime: `onSignInRequired` runs when nobody is signed in, and
 * `isDenied` turns on for a person without an instance role.
 */
export function useConsoleSession(onSignInRequired: () => void) {
  const controller = new AbortController();
  const session = shallowRef<ConsoleSession | null>(null);
  const isDenied = shallowRef(false);
  const failure = shallowRef<ApiFailure | null>(null);

  onMounted(async () => {
    const result = await consoleApi.getSession(controller.signal);
    if (controller.signal.aborted) return;
    if (result.ok) session.value = result.value;
    else if (result.failure.status === 401) onSignInRequired();
    else if (result.failure.status === 403) isDenied.value = true;
    else failure.value = result.failure;
  });
  onScopeDispose(() => controller.abort());

  return { session, isDenied, failure };
}
