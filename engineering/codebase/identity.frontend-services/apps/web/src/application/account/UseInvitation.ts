import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import type { InvitationPreviewDto } from '@wow-two-beta/fe-identity/contracts';
import { onMounted, onScopeDispose, shallowRef } from 'vue';
import { accountApi } from '../../integration/account';

/**
 * Loads what the invitation link `token` offers, and whether someone is signed in to accept it; with no token, nothing
 * loads and `isLoading` stays false. `accept` adds the signed-in person with the link's role.
 */
export function useInvitation(token: string | null) {
  const controller = new AbortController();
  const invitation = shallowRef<InvitationPreviewDto | null>(null);
  const failure = shallowRef<ApiFailure | null>(null);
  const isSignedIn = shallowRef(false);
  const isLoading = shallowRef(token !== null);

  onMounted(async () => {
    if (!token) return;
    const [preview, account] = await Promise.all([
      accountApi.getInvitation(token, controller.signal),
      accountApi.getAccountInfo(controller.signal),
    ]);
    if (controller.signal.aborted) return;
    if (preview.ok) invitation.value = preview.value;
    else failure.value = preview.failure;
    isSignedIn.value = account.ok;
    isLoading.value = false;
  });
  onScopeDispose(() => controller.abort());

  function accept() {
    return token
      ? accountApi.acceptInvitation(token, controller.signal)
      : Promise.resolve(ResultExtensions.ok(undefined));
  }

  return { invitation, failure, isSignedIn, isLoading, accept };
}
