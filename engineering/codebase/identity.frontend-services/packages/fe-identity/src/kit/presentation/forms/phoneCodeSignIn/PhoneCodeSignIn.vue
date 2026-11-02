<script setup lang="ts">
import { AppErrorFactory, ResultExtensions, type AppError } from '@wow-two-beta/fe/foundation/results';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { object, phone as phoneRule } from '@wow-two-beta/fe/foundation/validators';
import { CheckboxField, Field, PhoneInput, TextInput } from '@wow-two-beta/fe/presentation/forms';
import { shallowRef } from 'vue';
import type { AccountClient } from '../../../account';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';
import { PhoneSignInStep } from './PhoneSignInStep';

/**
 * Signs a person in by a code sent to their phone: the phone first, then the code. A phone with no linked Telegram
 * chat first opens the bot, where sharing the contact links it and brings the code.
 */
defineOptions({ name: 'PhoneCodeSignIn' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** The OIDC client the sign-in serves, recorded by the sign-in log. */
    clientId?: string | null;
    /** The ISO codes of the countries a phone may come from, in display order; every country when omitted. */
    countries?: ReadonlyArray<string>;
    /** The ISO code selected first; the first allowed country when omitted. */
    defaultCountry?: string;
    /** Whether to offer "Keep me signed in", as the declaration's `isPersistenceOffered` says. */
    isPersistenceOffered?: boolean;
  }>(),
  {
    client: undefined,
    baseUrl: undefined,
    clientId: null,
    countries: undefined,
    defaultCountry: undefined,
    isPersistenceOffered: false,
  },
);

const emit = defineEmits<{
  /** Fires once the session cookie is set. */
  signedIn: [];
}>();

const client = useAccountClient(props);
const t = useAccountText();
const step = shallowRef<PhoneSignInStep>(PhoneSignInStep.Phone);
/** The phone the current code was sent to. */
const phone = shallowRef('');
const telegramLinkUrl = shallowRef<string | null>(null);
const isResending = shallowRef(false);
const isResent = shallowRef(false);
const resendFailure = shallowRef<AppError | null>(null);

const notSent = () =>
  ResultExtensions.fail({
    ...AppErrorFactory.unavailable(),
    message: t('PhoneCodeSignIn.notSent', 'We could not send a code. Try again in a minute.'),
  });

async function send(value: string, signal?: AbortSignal) {
  const result = await client.sendPhoneSignInCode({ phone: value }, signal);
  if (!result.ok) return result;
  if (!result.value.isCodeSent && !result.value.telegramLinkUrl) return notSent();
  phone.value = value;
  telegramLinkUrl.value = result.value.telegramLinkUrl;
  return result;
}

const phoneForm = useAccountForm({
  defaultValues: { phone: '' },
  schema: object({ phone: phoneRule({ countries: props.countries }) }),
  onSubmit: async (values, { signal }) => {
    const result = await send(values.phone, signal);
    if (result.ok) {
      codeForm.reset();
      isResent.value = false;
      resendFailure.value = null;
      step.value = telegramLinkUrl.value ? PhoneSignInStep.Telegram : PhoneSignInStep.Code;
    }
    return result;
  },
});

const codeForm = useAccountForm({
  defaultValues: { code: '', isPersistent: false },
  onSubmit: async (values, { signal }) => {
    const result = await client.signInWithPhone(
      { phone: phone.value, code: values.code.trim(), isPersistent: props.isPersistenceOffered && values.isPersistent },
      { clientId: props.clientId, signal },
    );
    if (result.ok) emit('signedIn');
    return result;
  },
});

async function resend() {
  isResending.value = true;
  isResent.value = false;
  resendFailure.value = null;
  const result = await send(phone.value);
  isResending.value = false;
  if (result.ok) isResent.value = true;
  else resendFailure.value = result.failure;
}

function useAnotherPhone() {
  step.value = PhoneSignInStep.Phone;
  telegramLinkUrl.value = null;
}
</script>

<template>
  <form v-if="step === PhoneSignInStep.Phone" class="flex flex-col gap-4" novalidate @submit="phoneForm.handleSubmit">
    <phoneForm.Field v-slot="field" name="phone" is-required>
      <Field :label="t('PhoneCodeSignIn.phone', 'Phone number')">
        <PhoneInput
          v-model="field.value"
          name="phone"
          class="w-full [&>span]:shrink-0 [&>span]:whitespace-nowrap"
          :countries="countries"
          :default-country="defaultCountry"
          @blur="field.onBlur"
        />
      </Field>
    </phoneForm.Field>
    <FailureAlert :failure="phoneForm.state.submitError" />
    <Button type="submit" is-full-width :is-loading="phoneForm.state.isSubmitting">
      {{ t('PhoneCodeSignIn.send', 'Send a code') }}
    </Button>
  </form>

  <div v-else-if="step === PhoneSignInStep.Telegram" class="flex flex-col gap-4">
    <p class="text-sm text-muted-foreground">
      {{
        t(
          'PhoneCodeSignIn.telegramIntro',
          'Open our Telegram bot, press Start and share your contact. The bot then sends your code for {phone}.',
          { phone },
        )
      }}
    </p>
    <Button as-child is-full-width>
      <a
        :href="telegramLinkUrl ?? undefined"
        target="_blank"
        rel="noopener noreferrer"
        @click="step = PhoneSignInStep.Code"
      >
        {{ t('PhoneCodeSignIn.openTelegram', 'Open Telegram') }}
      </a>
    </Button>
    <Button type="button" variant="outline" tone="neutral" is-full-width @click="step = PhoneSignInStep.Code">
      {{ t('PhoneCodeSignIn.haveCode', 'I have the code') }}
    </Button>
    <Button type="button" variant="ghost" tone="neutral" @click="useAnotherPhone">
      {{ t('PhoneCodeSignIn.otherPhone', 'Use another number') }}
    </Button>
  </div>

  <form v-else class="flex flex-col gap-4" novalidate @submit="codeForm.handleSubmit">
    <p class="text-sm text-muted-foreground">
      <template v-if="telegramLinkUrl">
        {{ t('PhoneCodeSignIn.codeFromTelegram', 'Enter the code the bot sent you in Telegram.') }}
        <a
          :href="telegramLinkUrl"
          target="_blank"
          rel="noopener noreferrer"
          class="text-primary underline-offset-4 hover:underline"
        >
          {{ t('PhoneCodeSignIn.openTelegramAgain', 'Open Telegram') }}
        </a>
      </template>
      <template v-else>{{ t('PhoneCodeSignIn.codeSent', 'We sent a code to {phone}.', { phone }) }}</template>
    </p>
    <codeForm.Field v-slot="field" name="code" is-required>
      <Field :label="t('PhoneCodeSignIn.code', 'Code')">
        <TextInput
          v-model="field.value"
          name="code"
          inputmode="numeric"
          autocomplete="one-time-code"
          @blur="field.onBlur"
        />
      </Field>
    </codeForm.Field>
    <codeForm.Field v-if="isPersistenceOffered" v-slot="field" name="isPersistent">
      <CheckboxField
        v-model="field.value"
        :label="t('PhoneCodeSignIn.keepSignedIn', 'Keep me signed in')"
        @blur="field.onBlur"
      />
    </codeForm.Field>
    <FailureAlert :failure="codeForm.state.submitError" />
    <FailureAlert :failure="resendFailure" />
    <AlertSimple v-if="isResent" severity="success">
      {{ t('PhoneCodeSignIn.resent', 'We sent a new code.') }}
    </AlertSimple>
    <Button type="submit" is-full-width :is-loading="codeForm.state.isSubmitting">
      {{ t('PhoneCodeSignIn.submit', 'Sign in') }}
    </Button>
    <div class="flex flex-wrap justify-between gap-2">
      <Button type="button" variant="ghost" tone="neutral" :is-loading="isResending" @click="resend">
        {{ t('PhoneCodeSignIn.resend', 'Send a new code') }}
      </Button>
      <Button type="button" variant="ghost" tone="neutral" @click="useAnotherPhone">
        {{ t('PhoneCodeSignIn.otherPhone', 'Use another number') }}
      </Button>
    </div>
  </form>
</template>
