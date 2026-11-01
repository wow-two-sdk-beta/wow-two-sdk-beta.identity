import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { onMounted, onScopeDispose, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue';
import type { SignInOptions } from '../../domain/account';
import { signInOptionsApi } from '../../integration/account';

/**
 * Loads the sign-in declaration for this component's lifetime, naming `clientId` (or none) for the sign-in log, and
 * again when `clientId` changes. The declaration on screen stays until the next one arrives.
 */
export function useSignInOptions(clientId: MaybeRefOrGetter<string | null>) {
  const options = shallowRef<SignInOptions | null>(null);
  const failure = shallowRef<ApiFailure | null>(null);
  let controller: AbortController | null = null;

  async function load(id: string | null) {
    controller?.abort();
    const current = new AbortController();
    controller = current;
    const result = await signInOptionsApi.get(id, current.signal);
    if (current.signal.aborted) return;
    if (result.ok) {
      options.value = result.value;
      failure.value = null;
    } else failure.value = result.failure;
  }

  onMounted(() => void load(toValue(clientId)));
  watch(
    () => toValue(clientId),
    (id) => void load(id),
  );
  onScopeDispose(() => controller?.abort());

  return { options, failure };
}
