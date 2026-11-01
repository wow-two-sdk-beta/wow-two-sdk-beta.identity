<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { SignUpForm } from '@wow-two-beta/fe-identity/kit';
import { hostedHref, useSignInOptions } from '../../../application/account';
import type { SignInRequest } from '../../../domain/account';
import HostedLayout from '../../common/layout/HostedLayout.vue';

/** Creates an account while the instance keeps sign-up on. */
defineOptions({ name: 'SignUpPage' });

const props = defineProps<{
  /** Where the person continues once signed in, decoded from the route. */
  request: SignInRequest;
}>();

const { options } = useSignInOptions(props.request.clientId);
const signInHref = hostedHref(HostedPageRouteConstants.signIn, props.request.returnUrl);
</script>

<template>
  <HostedLayout :options="options" title="Sign up">
    <SignUpForm :sign-in-href="signInHref" />
    <template #footer>
      <p class="text-muted-foreground">
        Already have an account?
        <a :href="signInHref" class="font-medium text-primary underline-offset-4 hover:underline">Sign in</a>
      </p>
    </template>
  </HostedLayout>
</template>
