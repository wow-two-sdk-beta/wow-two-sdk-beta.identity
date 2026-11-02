<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { ResetPasswordForm } from '@wow-two-beta/fe-identity/kit';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { hostedHref, useHostedOptions } from '../../../application/account';
import HostedPage from '../../common/layout/HostedPage.vue';

/** Sets a new password from the emailed reset link, then offers the sign-in, which continues to the link's return URL. */
defineOptions({ name: 'ResetPasswordPage' });

const props = defineProps<{
  /** The email and token the link carried, or `null` for an incomplete link. */
  request: { readonly email: string; readonly token: string } | null;
  /** Where the person continues once signed in, from the link's `returnUrl`; `/` without one. */
  returnUrl: string;
}>();

const { options } = useHostedOptions();
const signInHref = hostedHref(HostedPageRouteConstants.signIn, props.returnUrl);
const forgotPasswordHref = hostedHref(HostedPageRouteConstants.forgotPassword, props.returnUrl);
</script>

<template>
  <HostedPage title="Choose a new password">
    <ResetPasswordForm
      v-if="request"
      :email="request.email"
      :token="request.token"
      :sign-in-href="signInHref"
      :password-rules="options?.passwordRules"
    />
    <AlertSimple v-else severity="danger">This link is incomplete. Open the link from the email again.</AlertSimple>
    <template #footer>
      <a :href="forgotPasswordHref" class="font-medium text-primary underline-offset-4 hover:underline">
        Send a new reset link
      </a>
    </template>
  </HostedPage>
</template>
