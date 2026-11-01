<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { EmailInput, Field, PasswordInput } from '@wow-two-beta/fe/presentation/forms';
import { shallowRef } from 'vue';
import type { RegisteredAccountDto } from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';

/** Creates an account with an email and password while the instance keeps sign-up on, then says what comes next. */
defineOptions({ name: 'SignUpForm' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** Where the person signs in once the account exists; no link when omitted. */
    signInHref?: string;
  }>(),
  { client: undefined, baseUrl: undefined, signInHref: undefined },
);

const emit = defineEmits<{
  /** Fires with the account a registration created. */
  signedUp: [account: RegisteredAccountDto];
}>();

const client = useAccountClient(props);
const t = useAccountText();
const registered = shallowRef<{ account: RegisteredAccountDto; email: string } | null>(null);

const form = useAccountForm({
  defaultValues: { email: '', password: '' },
  onSubmit: async (values, { signal }) => {
    const result = await client.register(values, signal);
    if (result.ok) {
      registered.value = { account: result.value, email: values.email };
      emit('signedUp', result.value);
    }
    return result;
  },
});
</script>

<template>
  <div v-if="registered" class="flex flex-col gap-4">
    <AlertSimple severity="success">
      <template v-if="registered.account.isConfirmationSent">
        {{
          t('SignUpForm.confirmationSent', 'Check your inbox: we sent a link to {email} to confirm the address.', {
            email: registered.email,
          })
        }}
      </template>
      <template v-else>{{ t('SignUpForm.created', 'Your account is ready.') }}</template>
    </AlertSimple>
    <Button v-if="signInHref" as-child is-full-width>
      <a :href="signInHref">{{ t('SignUpForm.signIn', 'Sign in') }}</a>
    </Button>
  </div>
  <form v-else class="flex flex-col gap-4" novalidate @submit="form.handleSubmit">
    <form.Field v-slot="field" name="email" is-required>
      <Field :label="t('SignUpForm.email', 'Email')">
        <EmailInput v-model="field.value" name="email" autocomplete="email" @blur="field.onBlur" />
      </Field>
    </form.Field>
    <form.Field v-slot="field" name="password" is-required>
      <Field :label="t('SignUpForm.password', 'Password')">
        <PasswordInput v-model="field.value" name="password" autocomplete="new-password" @blur="field.onBlur" />
      </Field>
    </form.Field>
    <FailureAlert :failure="form.state.submitError" />
    <Button type="submit" is-full-width :is-loading="form.state.isSubmitting">
      {{ t('SignUpForm.submit', 'Create the account') }}
    </Button>
  </form>
</template>
