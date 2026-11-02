<script setup lang="ts">
import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions } from '@wow-two-beta/fe/foundation/results';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { CheckboxField, Field, PasswordInput, TextInput } from '@wow-two-beta/fe/presentation/forms';
import { shallowRef } from 'vue';
import {
  IdentityErrorCodeConstants,
  type PasswordChangeRequiredDto,
  type SignInApiRequest,
  type TwoFactorChallengeDto,
} from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { readIdentityCode, readTwoFactorChallenge } from '../../../account/AccountProblems';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';
import { SecondFactorForm, type SecondFactor } from '../secondFactorForm';

/**
 * Signs a person in with an identifier and password, by cookie session — on the hosted page or embedded in a product.
 * An account with a second factor continues to a code or recovery code step before `signedIn` fires.
 */
defineOptions({ name: 'PasswordSignIn' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** The OIDC client the sign-in serves, recorded by the sign-in log. */
    clientId?: string | null;
    /** Where "Forgot your password?" leads; no link when omitted. */
    forgotPasswordHref?: string;
    /** The identifier the field starts with. */
    initialIdentifier?: string;
    /** Whether to offer "Keep me signed in", as the declaration's `isPersistenceOffered` says. */
    isPersistenceOffered?: boolean;
    /** The local path a resent confirmation link continues to once the person signs in; the service drops anything else. */
    returnUrl?: string | null;
  }>(),
  {
    client: undefined,
    baseUrl: undefined,
    clientId: null,
    forgotPasswordHref: undefined,
    initialIdentifier: '',
    isPersistenceOffered: false,
    returnUrl: null,
  },
);

const emit = defineEmits<{
  /** Fires once the session cookie is set. */
  signedIn: [];
  /** Fires when the password matched but breaks the current rules: no session, only the change's ticket. */
  passwordChangeRequired: [required: PasswordChangeRequiredDto];
}>();

const client = useAccountClient(props);
const t = useAccountText();
/** The email of an account that must confirm it before signing in, when the identifier was one. */
const unconfirmedEmail = shallowRef<string | null>(null);
const isResent = shallowRef(false);
const isResending = shallowRef(false);
/** The sign-in a second factor completes: the challenge, and the first factor's request it repeats without a ticket. */
const pending = shallowRef<{ challenge: TwoFactorChallengeDto; request: SignInApiRequest } | null>(null);

function signedIn(answer: PasswordChangeRequiredDto | null) {
  if (answer) emit('passwordChangeRequired', answer);
  else emit('signedIn');
}

/** Reads whether a refusal is the `403` of an unconfirmed email, which only an email identifier can resend to. */
function isUnconfirmedEmail(failure: ApiFailure, identifier: string) {
  return (
    failure.status === 403 &&
    readIdentityCode(failure) === IdentityErrorCodeConstants.emailConfirmationRequired &&
    identifier.includes('@')
  );
}

const form = useAccountForm({
  defaultValues: { identifier: props.initialIdentifier, password: '', isPersistent: false },
  onSubmit: async (values, { signal }) => {
    unconfirmedEmail.value = null;
    isResent.value = false;
    const request = { ...values, isPersistent: props.isPersistenceOffered && values.isPersistent };
    const result = await client.signIn(request, { clientId: props.clientId, signal });
    if (result.ok) {
      signedIn(result.value);
      return result;
    }
    const challenge = readTwoFactorChallenge(result.failure);
    if (challenge) {
      pending.value = { challenge, request };
      return ResultExtensions.ok(null);
    }
    if (isUnconfirmedEmail(result.failure, values.identifier)) unconfirmedEmail.value = values.identifier;
    return result;
  },
});

/**
 * Completes the sign-in with the second factor: by the challenge's ticket when it carried one, else by repeating the
 * password sign-in with the factor. Either way it keeps the first request's `isPersistent`.
 */
async function completeSecondFactor(factor: SecondFactor, signal: AbortSignal) {
  const current = pending.value;
  if (!current) return ResultExtensions.ok(null);
  const { challenge, request } = current;
  const method = challenge.twoFactorMethod ? { twoFactorMethod: challenge.twoFactorMethod } : {};
  if (challenge.userId && challenge.twoFactorTicket) {
    const completed = await client.signInWithSecondFactor(
      {
        userId: challenge.userId,
        ticket: challenge.twoFactorTicket,
        ...method,
        ...factor,
        isPersistent: request.isPersistent === true,
      },
      { clientId: props.clientId, signal },
    );
    if (completed.ok) signedIn(null);
    return completed;
  }
  const repeated = await client.signIn({ ...request, ...method, ...factor }, { clientId: props.clientId, signal });
  if (repeated.ok) signedIn(repeated.value);
  return repeated;
}

async function resendConfirmation() {
  if (!unconfirmedEmail.value) return;
  isResending.value = true;
  const result = await client.resendConfirmationEmail({
    email: unconfirmedEmail.value,
    ...(props.returnUrl ? { returnUrl: props.returnUrl } : {}),
  });
  isResending.value = false;
  isResent.value = result.ok;
}
</script>

<template>
  <SecondFactorForm
    v-if="pending"
    :challenge="pending.challenge"
    :complete="completeSecondFactor"
    @restart="pending = null"
  />
  <form v-else class="flex flex-col gap-4" novalidate @submit="form.handleSubmit">
    <form.Field v-slot="field" name="identifier" is-required>
      <Field :label="t('PasswordSignIn.identifier', 'Email or user name')">
        <TextInput v-model="field.value" name="identifier" autocomplete="username" @blur="field.onBlur" />
      </Field>
    </form.Field>
    <form.Field v-slot="field" name="password" is-required>
      <Field :label="t('PasswordSignIn.password', 'Password')">
        <PasswordInput v-model="field.value" name="password" autocomplete="current-password" @blur="field.onBlur" />
      </Field>
    </form.Field>
    <div
      v-if="isPersistenceOffered || forgotPasswordHref"
      class="-mt-1 flex flex-wrap items-center justify-between gap-x-4 gap-y-2"
    >
      <form.Field v-if="isPersistenceOffered" v-slot="field" name="isPersistent">
        <CheckboxField
          v-model="field.value"
          :label="t('PasswordSignIn.keepSignedIn', 'Keep me signed in')"
          @blur="field.onBlur"
        />
      </form.Field>
      <a
        v-if="forgotPasswordHref"
        :href="forgotPasswordHref"
        class="ms-auto text-sm text-primary underline-offset-4 hover:underline"
      >
        {{ t('PasswordSignIn.forgotPassword', 'Forgot your password?') }}
      </a>
    </div>
    <FailureAlert :failure="form.state.submitError" />
    <template v-if="unconfirmedEmail">
      <AlertSimple v-if="isResent" severity="success">
        {{
          t('PasswordSignIn.confirmationResent', 'We sent the confirmation link to {email} again.', {
            email: unconfirmedEmail,
          })
        }}
      </AlertSimple>
      <Button
        v-else
        type="button"
        variant="outline"
        tone="neutral"
        :is-loading="isResending"
        @click="resendConfirmation"
      >
        {{ t('PasswordSignIn.resendConfirmation', 'Send the confirmation email again') }}
      </Button>
    </template>
    <Button type="submit" is-full-width :is-loading="form.state.isSubmitting">
      {{ t('PasswordSignIn.submit', 'Sign in') }}
    </Button>
  </form>
</template>
