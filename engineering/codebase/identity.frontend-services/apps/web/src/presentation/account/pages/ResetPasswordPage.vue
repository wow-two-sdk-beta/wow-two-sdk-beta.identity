<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { ResetPasswordForm } from '@wow-two-beta/fe-identity/kit';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { useSignInOptions } from '../../../application/account';
import HostedLayout from '../../common/layout/HostedLayout.vue';

/** Sets a new password from the emailed reset link. */
defineOptions({ name: 'ResetPasswordPage' });

defineProps<{
  /** The email and token the link carried, or `null` for an incomplete link. */
  request: { readonly email: string; readonly token: string } | null;
}>();

const { options } = useSignInOptions(null);
const { signIn: signInHref, forgotPassword: forgotPasswordHref } = HostedPageRouteConstants;
</script>

<template>
  <HostedLayout :options="options" title="Choose a new password">
    <ResetPasswordForm v-if="request" :email="request.email" :token="request.token" :sign-in-href="signInHref" />
    <AlertSimple v-else severity="danger">This link is incomplete. Open the link from the email again.</AlertSimple>
    <template #footer>
      <a :href="forgotPasswordHref" class="font-medium text-primary underline-offset-4 hover:underline">
        Send a new reset link
      </a>
    </template>
  </HostedLayout>
</template>
