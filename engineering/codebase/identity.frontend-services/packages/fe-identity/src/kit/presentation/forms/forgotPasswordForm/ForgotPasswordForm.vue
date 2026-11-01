<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { EmailInput, Field } from '@wow-two-beta/fe/presentation/forms';
import { shallowRef } from 'vue';
import type { AccountClient } from '../../../account';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';

/** Sends a password reset link to an email; it says the same whether or not an account uses it. */
defineOptions({ name: 'ForgotPasswordForm' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** The email the field starts with. */
    initialEmail?: string;
  }>(),
  { client: undefined, baseUrl: undefined, initialEmail: '' },
);

const emit = defineEmits<{
  /** Fires once the reset email is requested. */
  sent: [email: string];
}>();

const client = useAccountClient(props);
const t = useAccountText();
const sentTo = shallowRef<string | null>(null);

const form = useAccountForm({
  defaultValues: { email: props.initialEmail },
  onSubmit: async (values, { signal }) => {
    const result = await client.forgotPassword(values, signal);
    if (result.ok) {
      sentTo.value = values.email;
      emit('sent', values.email);
    }
    return result;
  },
});
</script>

<template>
  <AlertSimple v-if="sentTo" severity="success">
    {{
      t('ForgotPasswordForm.sent', 'If an account uses {email}, we sent it a link to reset the password.', {
        email: sentTo,
      })
    }}
  </AlertSimple>
  <form v-else class="flex flex-col gap-4" novalidate @submit="form.handleSubmit">
    <form.Field v-slot="field" name="email" is-required>
      <Field :label="t('ForgotPasswordForm.email', 'Email')">
        <EmailInput v-model="field.value" name="email" autocomplete="email" @blur="field.onBlur" />
      </Field>
    </form.Field>
    <FailureAlert :failure="form.state.submitError" />
    <Button type="submit" is-full-width :is-loading="form.state.isSubmitting">
      {{ t('ForgotPasswordForm.submit', 'Send the reset link') }}
    </Button>
  </form>
</template>
