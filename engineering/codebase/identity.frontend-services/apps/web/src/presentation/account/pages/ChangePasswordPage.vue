<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { ChangePasswordForm } from '@wow-two-beta/fe-identity/kit';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { onMounted } from 'vue';
import { useSignInOptions } from '../../../application/account';
import HostedLayout from '../../common/layout/HostedLayout.vue';

/** Spends a password change link: its token comes from the URL fragment, which the page clears once read. */
defineOptions({ name: 'ChangePasswordPage' });

const props = defineProps<{
  /** The one-time token from the link's fragment, or `null` without one. */
  token: string | null;
  /** Clears the fragment, so the token leaves the address bar and the history. */
  onTokenRead: () => void;
}>();

/** Kept from the first render: clearing the fragment re-decodes the route without it. */
const token = props.token;
const { options } = useSignInOptions(null);
const signInHref = HostedPageRouteConstants.signIn;

onMounted(() => {
  if (token) props.onTokenRead();
});
</script>

<template>
  <HostedLayout :options="options" title="Change your password">
    <ChangePasswordForm v-if="token" :token="token" />
    <AlertSimple v-else severity="danger">This link is incomplete. Open the link from the message again.</AlertSimple>
    <template #footer>
      <a :href="signInHref" class="font-medium text-primary underline-offset-4 hover:underline">Sign in</a>
    </template>
  </HostedLayout>
</template>
