import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import type { ConfirmEmailApiRequest } from '@wow-two-beta/fe-identity/contracts';
import { onMounted, onScopeDispose, shallowRef } from 'vue';
import { accountApi } from '../../integration/account';

/** Confirms the email a confirmation link names, once, when the component mounts. */
export function useEmailConfirmation(request: ConfirmEmailApiRequest | null) {
  const controller = new AbortController();
  const isConfirming = shallowRef(request !== null);
  const isConfirmed = shallowRef(false);
  const failure = shallowRef<ApiFailure | null>(null);

  onMounted(async () => {
    if (!request) return;
    const result = await accountApi.confirmEmail(request, controller.signal);
    if (controller.signal.aborted) return;
    isConfirming.value = false;
    if (result.ok) isConfirmed.value = true;
    else failure.value = result.failure;
  });
  onScopeDispose(() => controller.abort());

  return { isConfirming, isConfirmed, failure };
}
