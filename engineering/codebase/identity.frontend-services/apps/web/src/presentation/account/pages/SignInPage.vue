<script setup lang="ts">
import { HostedPageRouteConstants, type PasswordChangeRequiredDto } from '@wow-two-beta/fe-identity/contracts';
import { ChangePasswordForm, PasswordSignIn, PhoneCodeSignIn } from '@wow-two-beta/fe-identity/kit';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { DividerLayout } from '@wow-two-beta/fe/presentation/layout';
import { shallowRef } from 'vue';
import { hostedHref, useSignInOptions } from '../../../application/account';
import { SignInMethodId, type SignInRequest } from '../../../domain/account';
import HostedLayout from '../../common/layout/HostedLayout.vue';

/**
 * Signs a person in by every enabled method, in the declared order, then continues to the return URL. A password that
 * breaks the current rules is changed first, with the sign-in's ticket, and then signs in anew.
 */
defineOptions({ name: 'SignInPage' });

const props = defineProps<{
  /** Where to continue and the app the sign-in serves, decoded from the route. */
  request: SignInRequest;
  /** Leaves for the return URL, which may be a server route. */
  onContinue: (url: string) => void;
}>();

const { options } = useSignInOptions(props.request.clientId);
/** The required change in progress: the sign-in set no session, only this ticket. */
const required = shallowRef<PasswordChangeRequiredDto | null>(null);
const isPasswordChanged = shallowRef(false);
const forgotPasswordHref = hostedHref(HostedPageRouteConstants.forgotPassword, props.request.returnUrl);
const signUpHref = hostedHref(HostedPageRouteConstants.signUp, props.request.returnUrl);

function continueToReturnUrl() {
  props.onContinue(props.request.returnUrl);
}

function changeRequired(change: PasswordChangeRequiredDto) {
  isPasswordChanged.value = false;
  required.value = change;
}

function passwordChanged() {
  required.value = null;
  isPasswordChanged.value = true;
}
</script>

<template>
  <HostedLayout :options="options" :title="required ? 'Change your password' : 'Sign in'">
    <ChangePasswordForm
      v-if="required"
      :ticket="required.ticket"
      :confirmation="required.confirmation"
      @changed="passwordChanged"
    />
    <template v-else-if="options">
      <AlertSimple v-if="isPasswordChanged" severity="success">
        Your password is changed. Sign in with the new one.
      </AlertSimple>
      <template v-for="(method, index) in options.methods" :key="method.id">
        <DividerLayout v-if="index > 0" label="or" />
        <section :aria-label="method.id === SignInMethodId.Password ? 'Password' : 'Phone'">
          <PasswordSignIn
            v-if="method.id === SignInMethodId.Password"
            :client-id="request.clientId"
            :forgot-password-href="forgotPasswordHref"
            @signed-in="continueToReturnUrl"
            @password-change-required="changeRequired"
          />
          <PhoneCodeSignIn
            v-else-if="method.id === SignInMethodId.PhoneCode"
            :client-id="request.clientId"
            :countries="method.countries"
            @signed-in="continueToReturnUrl"
          />
        </section>
      </template>
    </template>
    <template v-if="options?.isSignUpEnabled && !required" #footer>
      <p class="text-muted-foreground">
        New here?
        <a :href="signUpHref" class="font-medium text-primary underline-offset-4 hover:underline">Create an account</a>
      </p>
    </template>
  </HostedLayout>
</template>
