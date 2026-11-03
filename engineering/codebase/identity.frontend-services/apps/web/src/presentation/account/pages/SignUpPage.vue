<script setup lang="ts">
import { HostedPageRouteConstants, SignUpMode, type InvitationStatus } from '@wow-two-beta/fe-identity/contracts';
import { InviteSignUpForm, PhoneCodeSignIn, SignUpForm } from '@wow-two-beta/fe-identity/kit';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { DividerLayout } from '@wow-two-beta/fe/presentation/layout';
import { computed, shallowRef } from 'vue';
import { emailedReturnUrl, hostedHref, useHostedOptions, useInvitation } from '../../../application/account';
import { SignInMethodId, type SignInRequest } from '../../../domain/account';
import HostedPage from '../../common/layout/HostedPage.vue';

/**
 * Creates an account. With an invitation link's token it names the organization and role the link carries, then offers
 * the instance's methods carrying the token, or, to someone signed in, joining at once. The phone method there asks
 * for the declared profile and verifies the phone in Telegram with no code typed, continuing signed in. Without a
 * token it offers the email form while sign-up is open and the instance takes emails, the phone method on an open
 * phone-only instance; by invitation only, it says so. A link that names a trusted contact lets the person verify
 * the phone with that contact's code instead of their own Telegram.
 */
defineOptions({ name: 'SignUpPage' });

const props = defineProps<{
  /** Where the person continues once signed in, decoded from the route. */
  request: SignInRequest;
  /** The token of the invitation link that opened the page; `null` without one. */
  invitationToken: string | null;
  /** This page's own path with its query, where signing in first leads back to. */
  selfHref: string;
  /** Leaves for the return URL, which may be a server route. */
  onContinue: (url: string) => void;
  /** Opens another hosted page in the app. */
  onNavigate: (path: string) => void;
}>();

const { options, failure: optionsFailure } = useHostedOptions();
const { invitation, failure, isSignedIn, isLoading, accept } = useInvitation(props.invitationToken);
const signInHref = hostedHref(HostedPageRouteConstants.signIn, props.request.returnUrl);
const returnUrl = emailedReturnUrl(props.request.returnUrl);
/** Signing in first comes back here, to join with the link. */
const signInToJoinHref = hostedHref(HostedPageRouteConstants.signIn, props.selfHref);
const isJoining = shallowRef(false);
const joinFailure = shallowRef<string | null>(null);

const unavailable: Record<Exclude<InvitationStatus, 'pending'>, string> = {
  expired: 'This invitation has expired. Ask for a new link.',
  accepted: 'This invitation was already used. Ask for a new link.',
  revoked: 'This invitation was withdrawn.',
};

/** Whether the email and password form signs up: the password method is on and the instance takes emails. */
const isEmailSignUpOn = computed(
  () =>
    options.value?.isEmailEnabled === true &&
    options.value.methods.some((method) => method.id === SignInMethodId.Password),
);
/** Offers the form while sign-up is open by email, and while the declaration failed to load: the service then decides. */
const isFormOffered = computed(
  () => (options.value?.signUpMode === SignUpMode.Open && isEmailSignUpOn.value) || optionsFailure.value !== null,
);
/** The phone method an open, phone-only instance signs up with: its first sign-in creates the person. */
const openPhoneMethod = computed(() =>
  options.value?.signUpMode === SignUpMode.Open && !isEmailSignUpOn.value
    ? (options.value.methods.find((method) => method.id === SignInMethodId.PhoneCode) ?? null)
    : null,
);
const isPending = computed(() => invitation.value?.status === 'pending');
/** What the link offers, and the identifier it is for when it names one. */
const invitationText = computed(() => {
  const offer = invitation.value;
  if (!offer) return '';
  const hint = offer.phoneNumberHint ?? offer.emailHint;
  const invited = `You are invited to join ${offer.organizationName} as ${offer.role}.`;
  return hint ? `${invited} The link is for ${hint}.` : invited;
});
/** Signing in from a pending link comes back here to join; otherwise it continues as sign-in does. */
const footerSignInHref = computed(() =>
  props.invitationToken && isPending.value && !isSignedIn.value ? signInToJoinHref : signInHref,
);
/** Why the link no longer works; `null` while it is pending or not loaded. */
const unavailableMessage = computed(() => {
  const status = invitation.value?.status;
  return status && status !== 'pending' ? unavailable[status] : null;
});

function continueToReturnUrl() {
  props.onContinue(props.request.returnUrl);
}

/** Sends a person who joined but has a second factor to sign in, continuing to the return URL after. */
function signInToContinue() {
  props.onNavigate(signInHref);
}

async function join() {
  if (!props.invitationToken) return;
  isJoining.value = true;
  joinFailure.value = null;
  const result = await accept();
  isJoining.value = false;
  if (result.ok) continueToReturnUrl();
  else joinFailure.value = result.failure.message || 'We could not add you. Try again.';
}
</script>

<template>
  <HostedPage :title="invitationToken ? 'Join' : 'Sign up'">
    <template v-if="invitationToken">
      <p v-if="isLoading" class="text-sm text-muted-foreground">Reading the invitation…</p>
      <AlertSimple v-else-if="failure" severity="danger">
        This invitation link is not valid. Check the link, or ask for a new one.
      </AlertSimple>
      <AlertSimple v-else-if="unavailableMessage" severity="warning">{{ unavailableMessage }}</AlertSimple>
      <template v-else-if="invitation">
        <p class="text-sm">{{ invitationText }}</p>
        <template v-if="isSignedIn">
          <AlertSimple v-if="joinFailure" severity="danger">{{ joinFailure }}</AlertSimple>
          <Button type="button" is-full-width :is-loading="isJoining" @click="join">
            Join {{ invitation.organizationName }}
          </Button>
        </template>
        <template v-else-if="options">
          <template v-for="(method, index) in options.methods" :key="method.id">
            <DividerLayout v-if="index > 0" label="or" />
            <section :aria-label="method.id === SignInMethodId.Password ? 'Email' : 'Phone'">
              <SignUpForm
                v-if="method.id === SignInMethodId.Password && options.isEmailEnabled"
                :sign-in-href="signInHref"
                :password-rules="options.passwordRules"
                :invitation-token="invitationToken"
                :return-url="returnUrl"
              />
              <InviteSignUpForm
                v-else-if="method.id === SignInMethodId.PhoneCode"
                :invitation-token="invitationToken"
                :profile="options.signUpProfile"
                :client-id="request.clientId"
                :trusted-contact-hint="invitation.trustedContactHint"
                :code-rules="method.codeRules"
                :is-persistence-offered="options.isPersistenceOffered"
                @signed-up="continueToReturnUrl"
                @sign-in-required="signInToContinue"
              />
            </section>
          </template>
        </template>
      </template>
    </template>
    <SignUpForm
      v-else-if="isFormOffered"
      :sign-in-href="signInHref"
      :password-rules="options?.passwordRules"
      :return-url="returnUrl"
    />
    <PhoneCodeSignIn
      v-else-if="openPhoneMethod?.id === SignInMethodId.PhoneCode"
      :client-id="request.clientId"
      :countries="openPhoneMethod.countries"
      :code-rules="openPhoneMethod.codeRules"
      @signed-in="continueToReturnUrl"
    />
    <AlertSimple v-else-if="options" severity="info">
      Sign-up is by invitation only. Ask an admin of your organization for an invitation link.
    </AlertSimple>
    <template #footer>
      <p class="text-muted-foreground">
        Already have an account?
        <a :href="footerSignInHref" class="font-medium text-primary underline-offset-4 hover:underline">Sign in</a>
      </p>
    </template>
  </HostedPage>
</template>
