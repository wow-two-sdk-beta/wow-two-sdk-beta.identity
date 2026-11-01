<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { Field, PasswordInput } from '@wow-two-beta/fe/presentation/forms';
import { shallowRef } from 'vue';
import type { AccountClient } from '../../../account';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';

/** Sets a new password with the token a reset email carried. */
defineOptions({ name: 'ResetPasswordForm' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** The account email the reset link named. */
    email: string;
    /** The reset token the link carried. */
    token: string;
    /** Where the person signs in once the password is set; no link when omitted. */
    signInHref?: string;
  }>(),
  { client: undefined, baseUrl: undefined, signInHref: undefined },
);

const emit = defineEmits<{
  /** Fires once the new password is set. */
  reset: [];
}>();

const client = useAccountClient(props);
const t = useAccountText();
const isReset = shallowRef(false);

const form = useAccountForm({
  defaultValues: { newPassword: '' },
  onSubmit: async (values, { signal }) => {
    const result = await client.resetPassword(
      { email: props.email, resetToken: props.token, newPassword: values.newPassword },
      signal,
    );
    if (result.ok) {
      isReset.value = true;
      emit('reset');
    }
    return result;
  },
});
</script>

<template>
  <div v-if="isReset" class="flex flex-col gap-4">
    <AlertSimple severity="success">{{ t('ResetPasswordForm.done', 'Your password is changed.') }}</AlertSimple>
    <Button v-if="signInHref" as-child is-full-width>
      <a :href="signInHref">{{ t('ResetPasswordForm.signIn', 'Sign in') }}</a>
    </Button>
  </div>
  <form v-else class="flex flex-col gap-4" novalidate @submit="form.handleSubmit">
    <p class="text-sm text-muted-foreground">
      {{ t('ResetPasswordForm.intro', 'Choose a new password for {email}.', { email }) }}
    </p>
    <form.Field v-slot="field" name="newPassword" is-required>
      <Field :label="t('ResetPasswordForm.newPassword', 'New password')">
        <PasswordInput v-model="field.value" name="newPassword" autocomplete="new-password" @blur="field.onBlur" />
      </Field>
    </form.Field>
    <FailureAlert :failure="form.state.submitError" />
    <Button type="submit" is-full-width :is-loading="form.state.isSubmitting">
      {{ t('ResetPasswordForm.submit', 'Set the password') }}
    </Button>
  </form>
</template>
