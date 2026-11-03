<script setup lang="ts">
import {
  ChangePasswordForm,
  PhoneSettings,
  SessionList,
  TrustedByList,
  TrustedContactsList,
} from '@wow-two-beta/fe-identity/kit';
import { ApiFailureExtensions } from '@wow-two-beta/fe/foundation/http';
import { AlertSimple, Spinner } from '@wow-two-beta/fe/presentation/feedback';
import { computed } from 'vue';
import { useAccountInfo, useHostedOptions } from '../../../application/account';
import { SignInMethodId } from '../../../domain/account';
import HostedPage from '../../common/layout/HostedPage.vue';

/**
 * Shows the signed-in person's security settings: the password change, under the instance's confirmation, while the
 * instance signs in by password (a phone-only instance has no password to change); the own phone, added or changed
 * through the Telegram bot; the trusted contacts who receive the person's sign-in codes, revoked from a session signed
 * in with the own phone; the accounts that trust the person, which they can leave; and the live sessions, naming the
 * trusted contact one went through. A block whose route the service lacks stays out.
 */
defineOptions({ name: 'AccountSecurityPage' });

const props = defineProps<{
  /** Sends a signed-out person to the sign-in, coming back here. */
  onSignInRequired: () => void;
}>();

const { options } = useHostedOptions();
const { account, failure } = useAccountInfo(() => props.onSignInRequired());
const isPasswordOn = computed(
  () => options.value?.methods.some((method) => method.id === SignInMethodId.Password) ?? true,
);
</script>

<template>
  <HostedPage title="Security">
    <AlertSimple v-if="failure" severity="danger">{{ ApiFailureExtensions.reason(failure) }}</AlertSimple>
    <p v-else-if="!account" class="flex items-center gap-2 text-muted-foreground"><Spinner /> Loading your account…</p>
    <template v-else>
      <p class="text-sm text-muted-foreground">
        Signed in as <span class="font-medium text-foreground">{{ account.email ?? account.userName }}</span>
      </p>
      <section v-if="isPasswordOn" class="flex flex-col gap-3" aria-labelledby="password-heading">
        <h2 id="password-heading" class="text-lg font-semibold">Password</h2>
        <ChangePasswordForm :password-rules="options?.passwordRules" />
      </section>
      <p v-else class="text-sm text-muted-foreground">
        You sign in with your phone number; there is no password to change.
      </p>
      <PhoneSettings />
      <TrustedContactsList :is-empty-shown="options?.isTrustedContactSignInEnabled === true" />
      <TrustedByList />
      <SessionList />
    </template>
  </HostedPage>
</template>
