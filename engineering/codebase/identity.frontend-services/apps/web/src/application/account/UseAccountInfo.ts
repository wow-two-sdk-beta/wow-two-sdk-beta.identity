import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import type { AccountInfoDto } from '@wow-two-beta/fe-identity/contracts';
import { onMounted, onScopeDispose, shallowRef } from 'vue';
import { accountApi } from '../../integration/account';

/** Loads the signed-in account; `onSignedOut` runs when nobody is signed in. */
export function useAccountInfo(onSignedOut: () => void) {
  const controller = new AbortController();
  const account = shallowRef<AccountInfoDto | null>(null);
  const failure = shallowRef<ApiFailure | null>(null);

  onMounted(async () => {
    const result = await accountApi.getAccountInfo(controller.signal);
    if (controller.signal.aborted) return;
    if (result.ok) account.value = result.value;
    else if (result.failure.status === 401) onSignedOut();
    else failure.value = result.failure;
  });
  onScopeDispose(() => controller.abort());

  return { account, failure };
}
