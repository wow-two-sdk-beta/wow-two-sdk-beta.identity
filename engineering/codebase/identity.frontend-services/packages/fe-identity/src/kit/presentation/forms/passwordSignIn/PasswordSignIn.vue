<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { CheckboxField, Field, PasswordInput, TextInput } from '@wow-two-beta/fe/presentation/forms';
import { shallowRef } from 'vue';
import type { PasswordChangeRequiredDto } from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';

/** Signs a person in with an identifier and password, by cookie session — on the hosted page or embedded in a product. */
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
  }>(),
  {
    client: undefined,
    baseUrl: undefined,
    clientId: null,
    forgotPasswordHref: undefined,
    initialIdentifier: '',
    isPersistenceOffered: false,
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

const form = useAccountForm({
  defaultValues: { identifier: props.initialIdentifier, password: '', isPersistent: false },
  onSubmit: async (values, { signal }) => {
    unconfirmedEmail.value = null;
    isResent.value = false;
    const request = { ...values, isPersistent: props.isPersistenceOffered && values.isPersistent };
    const result = await client.signIn(request, { clientId: props.clientId, signal });
    if (result.ok) {
      if (result.value) emit('passwordChangeRequired', result.value);
      else emit('signedIn');
    } else if (result.failure.status === 403 && values.identifier.includes('@')) {
      unconfirmedEmail.value = values.identifier;
    }
    return result;
  },
});

async function resendConfirmation() {
  if (!unconfirmedEmail.value) return;
  isResending.value = true;
  const result = await client.resendConfirmationEmail({ email: unconfirmedEmail.value });
  isResending.value = false;
  isResent.value = result.ok;
}
</script>

<template>
  <form class="flex flex-col gap-4" novalidate @submit="form.handleSubmit">
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
