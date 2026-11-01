<script setup lang="ts">
import { ChangePasswordForm } from '@wow-two-beta/fe-identity/kit';
import { ApiFailureExtensions } from '@wow-two-beta/fe/foundation/http';
import { AlertSimple, Spinner } from '@wow-two-beta/fe/presentation/feedback';
import { useAccountInfo, useSignInOptions } from '../../../application/account';
import HostedLayout from '../../common/layout/HostedLayout.vue';

/** Shows the signed-in person's security settings: the password change, under the instance's confirmation. */
defineOptions({ name: 'AccountSecurityPage' });

const props = defineProps<{
  /** Sends a signed-out person to the sign-in, coming back here. */
  onSignInRequired: () => void;
}>();

const { options } = useSignInOptions(null);
const { account, failure } = useAccountInfo(() => props.onSignInRequired());
</script>

<template>
  <HostedLayout :options="options" title="Security">
    <AlertSimple v-if="failure" severity="danger">{{ ApiFailureExtensions.reason(failure) }}</AlertSimple>
    <p v-else-if="!account" class="flex items-center gap-2 text-muted-foreground"><Spinner /> Loading your account…</p>
    <template v-else>
      <p class="text-sm text-muted-foreground">
        Signed in as <span class="font-medium text-foreground">{{ account.email ?? account.userName }}</span>
      </p>
      <section class="flex flex-col gap-3" aria-labelledby="password-heading">
        <h2 id="password-heading" class="text-lg font-semibold">Password</h2>
        <ChangePasswordForm />
      </section>
    </template>
  </HostedLayout>
</template>
