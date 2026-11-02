<script setup lang="ts">
import {
  HostedPageRouteConstants,
  SignUpMode,
  type PasswordChangeRequiredDto,
} from '@wow-two-beta/fe-identity/contracts';
import { ChangePasswordForm, PasswordSignIn, PhoneCodeSignIn } from '@wow-two-beta/fe-identity/kit';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { DividerLayout } from '@wow-two-beta/fe/presentation/layout';
import { shallowRef } from 'vue';
import { emailedReturnUrl, hostedHref, useHostedOptions } from '../../../application/account';
import { SignInMethodId, type SignInRequest } from '../../../domain/account';
import HostedPage from '../../common/layout/HostedPage.vue';

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

const { options, failure } = useHostedOptions();
/** The required change in progress: the sign-in set no session, only this ticket. */
const required = shallowRef<PasswordChangeRequiredDto | null>(null);
const isPasswordChanged = shallowRef(false);
const forgotPasswordHref = hostedHref(HostedPageRouteConstants.forgotPassword, props.request.returnUrl);
const signUpHref = hostedHref(HostedPageRouteConstants.signUp, props.request.returnUrl);
const returnUrl = emailedReturnUrl(props.request.returnUrl);

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
  <HostedPage :title="required ? 'Change your password' : 'Sign in'">
    <ChangePasswordForm
      v-if="required"
      :ticket="required.ticket"
      :confirmation="required.confirmation"
      :password-rules="options?.passwordRules"
      @changed="passwordChanged"
    />
    <AlertSimple v-else-if="failure" severity="danger">
      We could not load the ways to sign in. Reload the page to try again.
    </AlertSimple>
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
            :is-persistence-offered="options.isPersistenceOffered"
            :return-url="returnUrl"
            @signed-in="continueToReturnUrl"
            @password-change-required="changeRequired"
          />
          <PhoneCodeSignIn
            v-else-if="method.id === SignInMethodId.PhoneCode"
            :client-id="request.clientId"
            :countries="method.countries"
            :code-rules="method.codeRules"
            :is-persistence-offered="options.isPersistenceOffered"
            @signed-in="continueToReturnUrl"
          />
        </section>
      </template>
    </template>
    <template v-if="options?.signUpMode === SignUpMode.Open && !required" #footer>
      <p class="text-muted-foreground">
        New here?
        <a :href="signUpHref" class="font-medium text-primary underline-offset-4 hover:underline">Sign up</a>
      </p>
    </template>
  </HostedPage>
</template>
