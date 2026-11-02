<script setup lang="ts">
import type { AppError, Result } from '@wow-two-beta/fe/foundation/results';
import { object } from '@wow-two-beta/fe/foundation/validators';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Field, TextInput } from '@wow-two-beta/fe/presentation/forms';
import { computed, shallowRef } from 'vue';
import type { TwoFactorChallengeDto } from '../../../../contracts';
import { useAccountForm, useAccountText } from '../../../account/hooks';
import { requiredText } from '../../../account/validation';
import { FailureAlert } from '../../feedback/failureAlert';
import type { SecondFactor } from './SecondFactor';

/**
 * Takes the second factor a sign-in's `TwoFactorRequired` challenge asks for: a code, or a recovery code instead. The
 * sign-in form that met the challenge sends it, so this step stays the same for every first factor.
 */
defineOptions({ name: 'SecondFactorForm' });

const props = defineProps<{
  /** The challenge the first factor answered with. */
  challenge: TwoFactorChallengeDto;
  /** Completes the sign-in with the entered factor; a failure shows above the button. */
  complete: (factor: SecondFactor, signal: AbortSignal) => Promise<Result<unknown, AppError>>;
}>();

const emit = defineEmits<{
  /** Fires when the person starts the sign-in again. */
  restart: [];
}>();

const t = useAccountText();
const isRecoveryCode = shallowRef(false);
const isAuthenticator = computed(
  () => !props.challenge.twoFactorMethod || props.challenge.twoFactorMethod.toLowerCase() === 'authenticator',
);

const form = useAccountForm({
  defaultValues: { code: '' },
  schema: object({ code: requiredText() }),
  onSubmit: (values, { signal }) => {
    const code = values.code.trim();
    return props.complete(isRecoveryCode.value ? { recoveryCode: code } : { twoFactorCode: code }, signal);
  },
});

function toggleRecoveryCode() {
  isRecoveryCode.value = !isRecoveryCode.value;
  form.reset();
}
</script>

<template>
  <form class="flex flex-col gap-4" novalidate @submit="form.handleSubmit">
    <p class="text-sm text-muted-foreground">
      <template v-if="isRecoveryCode">
        {{ t('SecondFactorForm.recoveryIntro', 'Enter one of the recovery codes you saved. Each code works once.') }}
      </template>
      <template v-else-if="isAuthenticator">
        {{ t('SecondFactorForm.authenticatorIntro', 'Enter the code from your authenticator app.') }}
      </template>
      <template v-else>{{ t('SecondFactorForm.sentIntro', 'Enter the code we sent you.') }}</template>
    </p>
    <form.Field v-slot="field" name="code" is-required>
      <Field
        :label="
          isRecoveryCode ? t('SecondFactorForm.recoveryCode', 'Recovery code') : t('SecondFactorForm.code', 'Code')
        "
      >
        <TextInput
          v-model="field.value"
          :name="isRecoveryCode ? 'recoveryCode' : 'twoFactorCode'"
          :inputmode="isRecoveryCode ? 'text' : 'numeric'"
          autocomplete="one-time-code"
          @blur="field.onBlur"
        />
      </Field>
    </form.Field>
    <FailureAlert :failure="form.state.submitError" />
    <Button type="submit" is-full-width :is-loading="form.state.isSubmitting">
      {{ t('SecondFactorForm.submit', 'Sign in') }}
    </Button>
    <div class="flex flex-wrap justify-between gap-2">
      <Button type="button" variant="ghost" tone="neutral" @click="toggleRecoveryCode">
        <template v-if="isRecoveryCode">{{ t('SecondFactorForm.useCode', 'Use a code instead') }}</template>
        <template v-else>{{ t('SecondFactorForm.useRecoveryCode', 'Use a recovery code') }}</template>
      </Button>
      <Button type="button" variant="ghost" tone="neutral" @click="emit('restart')">
        {{ t('SecondFactorForm.restart', 'Start again') }}
      </Button>
    </div>
  </form>
</template>
