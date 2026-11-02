<script setup lang="ts">
import { HostedPageRouteConstants, type ConfirmEmailApiRequest } from '@wow-two-beta/fe-identity/contracts';
import { ApiFailureExtensions } from '@wow-two-beta/fe/foundation/http';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple, Spinner } from '@wow-two-beta/fe/presentation/feedback';
import { hostedHref, useEmailConfirmation } from '../../../application/account';
import HostedPage from '../../common/layout/HostedPage.vue';

/** Confirms the email an emailed link names, then offers the sign-in, which continues to the link's return URL. */
defineOptions({ name: 'ConfirmEmailPage' });

const props = defineProps<{
  /** The account and token the link carried, or `null` for an incomplete link. */
  request: ConfirmEmailApiRequest | null;
  /** Where the person continues once signed in, from the link's `returnUrl`; `/` without one. */
  returnUrl: string;
}>();

const { isConfirming, isConfirmed, failure } = useEmailConfirmation(props.request);
const signInHref = hostedHref(HostedPageRouteConstants.signIn, props.returnUrl);
</script>

<template>
  <HostedPage title="Confirm your email">
    <AlertSimple v-if="!request" severity="danger">
      This link is incomplete. Open the link from the email again.
    </AlertSimple>
    <p v-else-if="isConfirming" class="flex items-center gap-2 text-muted-foreground">
      <Spinner /> Confirming your email…
    </p>
    <template v-else-if="isConfirmed">
      <AlertSimple severity="success">Your email is confirmed.</AlertSimple>
      <Button as-child is-full-width><a :href="signInHref">Sign in</a></Button>
    </template>
    <AlertSimple v-else severity="danger">
      {{ ApiFailureExtensions.reason(failure, { fallback: 'This link is invalid or has expired.' }) }}
    </AlertSimple>
  </HostedPage>
</template>
