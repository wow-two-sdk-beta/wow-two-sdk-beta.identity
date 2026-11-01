<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { Field, PasswordInput, TextInput } from '@wow-two-beta/fe/presentation/forms';
import { computed, onMounted, shallowRef } from 'vue';
import { PasswordChangeConfirmationType, type PasswordChangeConfirmationDto } from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';
import { ChangePasswordStage } from './ChangePasswordStage';

/**
 * Changes a password under the instance's confirmation setting. Signed in, it starts the change and asks for the
 * current password, plus the sent code under `Code`, or says a link went out under `Link`. With the ticket of a
 * sign-in that requires the change, it starts at once and skips the current password. With the token a change link
 * carried, it asks only for the new password.
 */
defineOptions({ name: 'ChangePasswordForm' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** The one-time token a change link carried. */
    token?: string | null;
    /** The ticket a sign-in answered with when the password breaks the current rules. */
    ticket?: string | null;
    /** How the ticket's change is confirmed, as the sign-in answered; asked of the instance when omitted. */
    confirmation?: PasswordChangeConfirmationType | null;
  }>(),
  { client: undefined, baseUrl: undefined, token: null, ticket: null, confirmation: null },
);

const emit = defineEmits<{
  /** Fires once the password is changed. */
  changed: [];
}>();

const client = useAccountClient(props);
const t = useAccountText();
/** The ticketed change under `None` needs no round trip: the sign-in already said so, and nothing is sent. */
const isOpenAtOnce = Boolean(props.ticket) && props.confirmation === PasswordChangeConfirmationType.None;
const stage = shallowRef<ChangePasswordStage>(
  props.token || isOpenAtOnce ? ChangePasswordStage.Form : ChangePasswordStage.Idle,
);
/** How the change is confirmed, once known, and whether its code or link went out. */
const started = shallowRef<PasswordChangeConfirmationDto | null>(
  isOpenAtOnce ? { confirmation: PasswordChangeConfirmationType.None, isSent: false } : null,
);
const isStarting = shallowRef(false);
const startFailure = shallowRef<AppError | null>(null);
const isCodeRequired = computed(() => started.value?.confirmation === PasswordChangeConfirmationType.Code);

const form = useAccountForm({
  defaultValues: { currentPassword: '', newPassword: '', code: '' },
  onSubmit: async (values, { signal }) => {
    const result = props.token
      ? await client.changePasswordWithLink({ token: props.token, newPassword: values.newPassword }, signal)
      : await client.changePassword(
          {
            ...(props.ticket ? { ticket: props.ticket } : { currentPassword: values.currentPassword }),
            newPassword: values.newPassword,
            ...(isCodeRequired.value ? { code: values.code.trim() } : {}),
          },
          signal,
        );
    if (result.ok) {
      stage.value = ChangePasswordStage.Done;
      emit('changed');
    }
    return result;
  },
});

/** Asks the instance how the change is confirmed, which sends the code or link that setting calls for. */
async function start() {
  isStarting.value = true;
  startFailure.value = null;
  const result = await client.startPasswordChange(props.ticket ? { ticket: props.ticket } : undefined);
  isStarting.value = false;
  if (!result.ok) {
    startFailure.value = result.failure;
    return;
  }
  started.value = result.value;
  stage.value =
    result.value.confirmation === PasswordChangeConfirmationType.Link
      ? ChangePasswordStage.LinkSent
      : ChangePasswordStage.Form;
}

onMounted(() => {
  if (props.ticket && !isOpenAtOnce) void start();
});
</script>

<template>
  <div class="flex flex-col gap-4">
    <p v-if="ticket && stage !== ChangePasswordStage.Done" class="text-sm text-muted-foreground">
      {{
        t(
          'ChangePasswordForm.required',
          'Your password no longer meets the rules here. Choose a new one, then sign in with it.',
        )
      }}
    </p>

    <template v-if="stage === ChangePasswordStage.Idle">
      <FailureAlert :failure="startFailure" />
      <Button type="button" is-full-width :is-loading="isStarting" @click="start">
        {{ t('ChangePasswordForm.start', 'Change the password') }}
      </Button>
    </template>

    <template v-else-if="stage === ChangePasswordStage.LinkSent">
      <AlertSimple v-if="started?.isSent" severity="info">
        {{ t('ChangePasswordForm.linkSent', 'We sent you a link to change the password. Open it to continue.') }}
      </AlertSimple>
      <AlertSimple v-else severity="warning">
        {{ t('ChangePasswordForm.linkNotSent', 'We could not send the link. Try again in a minute.') }}
      </AlertSimple>
      <FailureAlert :failure="startFailure" />
      <Button type="button" variant="outline" tone="neutral" :is-loading="isStarting" @click="start">
        {{ t('ChangePasswordForm.resendLink', 'Send the link again') }}
      </Button>
    </template>

    <form
      v-else-if="stage === ChangePasswordStage.Form"
      class="flex flex-col gap-4"
      novalidate
      @submit="form.handleSubmit"
    >
      <template v-if="isCodeRequired">
        <AlertSimple v-if="started?.isSent" severity="info">
          {{ t('ChangePasswordForm.codeSent', 'We sent you a code. Enter it beside the new password.') }}
        </AlertSimple>
        <AlertSimple v-else severity="warning">
          {{ t('ChangePasswordForm.codeNotSent', 'We could not send a code. Try again in a minute.') }}
        </AlertSimple>
      </template>
      <form.Field v-if="!token && !ticket" v-slot="field" name="currentPassword" is-required>
        <Field :label="t('ChangePasswordForm.currentPassword', 'Current password')">
          <PasswordInput
            v-model="field.value"
            name="currentPassword"
            autocomplete="current-password"
            @blur="field.onBlur"
          />
        </Field>
      </form.Field>
      <form.Field v-slot="field" name="newPassword" is-required>
        <Field :label="t('ChangePasswordForm.newPassword', 'New password')">
          <PasswordInput v-model="field.value" name="newPassword" autocomplete="new-password" @blur="field.onBlur" />
        </Field>
      </form.Field>
      <form.Field v-if="isCodeRequired" v-slot="field" name="code" is-required>
        <Field :label="t('ChangePasswordForm.code', 'Code')">
          <TextInput
            v-model="field.value"
            name="code"
            inputmode="numeric"
            autocomplete="one-time-code"
            @blur="field.onBlur"
          />
        </Field>
      </form.Field>
      <FailureAlert :failure="form.state.submitError" />
      <FailureAlert :failure="startFailure" />
      <Button type="submit" is-full-width :is-loading="form.state.isSubmitting">
        {{ t('ChangePasswordForm.submit', 'Change the password') }}
      </Button>
      <Button
        v-if="isCodeRequired"
        type="button"
        variant="ghost"
        tone="neutral"
        :is-loading="isStarting"
        @click="start"
      >
        {{ t('ChangePasswordForm.resendCode', 'Send a new code') }}
      </Button>
    </form>

    <AlertSimple v-else severity="success">
      {{ t('ChangePasswordForm.done', 'Your password is changed.') }}
    </AlertSimple>
  </div>
</template>
