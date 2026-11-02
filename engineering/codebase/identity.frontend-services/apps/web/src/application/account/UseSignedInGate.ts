import { onMounted, onScopeDispose, shallowRef } from 'vue';
import { accountApi } from '../../integration/account';
import { isFreshSignInRequested } from './ReadAccountRequests';

/**
 * Keeps a person who is signed in already off the sign-in methods: on mount it reads the session once and, when someone
 * is signed in, runs `onSignedIn` to continue to the return URL; `isChecking` holds until it knows, and stays on while
 * the page leaves. A return URL asking for a fresh sign-in (`prompt=login`, `max_age`) skips the check and shows the
 * methods at once, as does any failure to read the session. Signing out happens in the console.
 */
export function useSignedInGate(returnUrl: string, onSignedIn: () => void) {
  const controller = new AbortController();
  const isChecking = shallowRef(!isFreshSignInRequested(returnUrl));

  onMounted(async () => {
    if (!isChecking.value) return;
    const result = await accountApi.getAccountInfo(controller.signal);
    if (controller.signal.aborted) return;
    if (result.ok) onSignedIn();
    else isChecking.value = false;
  });
  onScopeDispose(() => controller.abort());

  return { isChecking };
}
