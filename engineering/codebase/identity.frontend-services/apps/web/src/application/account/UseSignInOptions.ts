import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { onMounted, onScopeDispose, shallowRef } from 'vue';
import type { SignInOptions } from '../../domain/account';
import { signInOptionsApi } from '../../integration/account';

/** Loads the sign-in declaration for this component's lifetime, naming `clientId` (or none) for the sign-in log. */
export function useSignInOptions(clientId: string | null) {
  const controller = new AbortController();
  const options = shallowRef<SignInOptions | null>(null);
  const failure = shallowRef<ApiFailure | null>(null);

  onMounted(async () => {
    const result = await signInOptionsApi.get(clientId, controller.signal);
    if (controller.signal.aborted) return;
    if (result.ok) options.value = result.value;
    else failure.value = result.failure;
  });
  onScopeDispose(() => controller.abort());

  return { options, failure };
}
