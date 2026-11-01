<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { ForgotPasswordForm } from '@wow-two-beta/fe-identity/kit';
import { hostedHref } from '../../../application/account';
import type { SignInRequest } from '../../../domain/account';
import HostedPage from '../../common/layout/HostedPage.vue';

/** Sends a password reset link. */
defineOptions({ name: 'ForgotPasswordPage' });

const props = defineProps<{
  /** Where the person continues once signed in, decoded from the route. */
  request: SignInRequest;
}>();

const signInHref = hostedHref(HostedPageRouteConstants.signIn, props.request.returnUrl);
</script>

<template>
  <HostedPage title="Reset your password">
    <ForgotPasswordForm />
    <template #footer>
      <a :href="signInHref" class="font-medium text-primary underline-offset-4 hover:underline">Back to sign-in</a>
    </template>
  </HostedPage>
</template>
