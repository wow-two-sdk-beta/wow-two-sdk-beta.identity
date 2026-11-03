<script setup lang="ts">
import { AppErrorFactory, ResultExtensions, type AppError } from '@wow-two-beta/fe/foundation/results';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Avatar, Badge } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { boolean, object, phone as phoneRule } from '@wow-two-beta/fe/foundation/validators';
import {
  CheckboxField,
  Field,
  PhoneInput,
  RadioField,
  RadioGroup,
  TextInput,
} from '@wow-two-beta/fe/presentation/forms';
import { computed, shallowRef } from 'vue';
import type {
  PhoneSignInAccountChoiceDto,
  PhoneSignInAccountDto,
  PhoneSignInTargetDto,
  SignInRuleDto,
  TwoFactorChallengeDto,
} from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { readTwoFactorChallenge } from '../../../account/AccountProblems';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { codeRule, passwordRuleMessages } from '../../../account/validation';
import { FailureAlert } from '../../feedback/failureAlert';
import { SecondFactorForm, type SecondFactor } from '../secondFactorForm';
import { PhoneSignInStep } from './PhoneSignInStep';

/**
 * Signs a person in by a code sent to their phone: the phone first, then the code. A phone with no linked Telegram
 * chat first opens the bot, where sharing the contact links it and brings the code. A code that breaks `codeRules`
 * shows under its field and spends no attempt; an account with a second factor continues to a code or recovery code
 * step before `signedIn` fires.
 *
 * With `isTrustedContactSignInEnabled`, the phone is followed by a check of its trusted contacts: a phone that has some
 * first offers "My Telegram" (the default) or each contact's masked label as the place the code goes. A phone whose
 * person is another's trusted contact continues, after the code, to a choice of the account to sign in as.
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
    /** The rules the code meets, the declared `code` field's `rules`, such as its length; with none, it is only required. */
    codeRules?: ReadonlyArray<SignInRuleDto>;
    /** Whether to offer "Keep me signed in", as the declaration's `isPersistenceOffered` says. */
    isPersistenceOffered?: boolean;
    /** Whether to offer the phone's trusted contacts as the code's destination, as the declaration's `isTrustedContactSignInEnabled` says. */
    isTrustedContactSignInEnabled?: boolean;
    /**
     * The token of an invitation link: the person joins its organization with its role, and a first sign-in creates them
     * even while sign-up is by invitation only.
     */
    invitationToken?: string | null;
  }>(),
  {
    client: undefined,
    baseUrl: undefined,
    clientId: null,
    countries: undefined,
    defaultCountry: undefined,
    codeRules: undefined,
    isPersistenceOffered: false,
    isTrustedContactSignInEnabled: false,
    invitationToken: null,
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
/** The second factor's challenge and whether the code step asked to stay signed in, once the code checked out. */
const pending = shallowRef<{ challenge: TwoFactorChallengeDto; isPersistent: boolean } | null>(null);
/** The destination value of the phone's own Telegram; a contact's is `contact:{id}`, which no opaque id can equal. */
const ownDestination = 'own';
const contactPrefix = 'contact:';
/** The phone's trusted contacts the code may go to instead, once the phone was checked. */
const targets = shallowRef<ReadonlyArray<PhoneSignInTargetDto>>([]);
const destination = shallowRef(ownDestination);
/** The label of the contact the last code went to; `null` while it went to the phone's own Telegram. */
const sentToLabel = shallowRef<string | null>(null);
const isSendingToDestination = shallowRef(false);
const destinationFailure = shallowRef<AppError | null>(null);
/** The account choice of a trusted contact and whether the code step asked to stay signed in. */
const choice = shallowRef<(PhoneSignInAccountChoiceDto & { isPersistent: boolean }) | null>(null);
/** The account being signed in as, while its request runs. */
const choosing = shallowRef<string | null>(null);
const chooseFailure = shallowRef<AppError | null>(null);

/** The id of the contact the picked destination names; none for the phone's own Telegram. */
const targetId = computed(() =>
  destination.value.startsWith(contactPrefix) ? destination.value.slice(contactPrefix.length) : undefined,
);

const notSent = () =>
  ResultExtensions.fail({
    ...AppErrorFactory.unavailable(),
    message: t('PhoneCodeSignIn.notSent', 'We could not send a code. Try again in a minute.'),
  });

/** Sends a code to the phone's own Telegram, or to the trusted contact `target` names. */
async function send(value: string, signal?: AbortSignal, target?: string) {
  const result = await client.sendPhoneSignInCode({ phone: value, ...(target ? { targetId: target } : {}) }, signal);
  if (!result.ok) return result;
  if (!result.value.isCodeSent && !result.value.telegramLinkUrl) return notSent();
  phone.value = value;
  telegramLinkUrl.value = result.value.telegramLinkUrl;
  sentToLabel.value = target ? (targets.value.find((candidate) => candidate.id === target)?.label ?? null) : null;
  return result;
}

const phoneForm = useAccountForm({
  defaultValues: { phone: '' },
  schema: object({ phone: phoneRule({ countries: props.countries }) }),
  onSubmit: async (values, { signal }) => {
    targets.value = [];
    destination.value = ownDestination;
    if (props.isTrustedContactSignInEnabled) {
      const found = await client.getPhoneSignInTargets({ phone: values.phone }, signal);
      if (!found.ok) return found;
      if (found.value.trustedContacts.length > 0) {
        phone.value = values.phone;
        targets.value = found.value.trustedContacts;
        destinationFailure.value = null;
        step.value = PhoneSignInStep.Target;
        return found;
      }
    }
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

/** Follows `codeRules`, which the page may load after the form shows. */
const codeSchema = computed(() => object({ code: codeRule(props.codeRules), isPersistent: boolean() }));

const codeForm = useAccountForm(() => ({
  defaultValues: { code: '', isPersistent: false },
  schema: codeSchema.value,
  messages: passwordRuleMessages,
  onSubmit: async (values, { signal }) => {
    const isPersistent = props.isPersistenceOffered && values.isPersistent;
    const result = await client.signInWithPhone(
      {
        phone: phone.value,
        code: values.code.trim(),
        isPersistent,
        ...(props.invitationToken ? { invitationToken: props.invitationToken } : {}),
      },
      { clientId: props.clientId, signal },
    );
    if (result.ok) {
      if (result.value) {
        // A trusted contact: no session yet, the account to sign in as comes first.
        choice.value = { ...result.value, isPersistent };
        chooseFailure.value = null;
        step.value = PhoneSignInStep.Account;
        return ResultExtensions.ok(undefined);
      }
      emit('signedIn');
      return result;
    }
    // The code is spent once it checked out, so only a challenge with its ticket can finish the sign-in.
    const challenge = readTwoFactorChallenge(result.failure);
    if (challenge?.userId && challenge.twoFactorTicket) {
      pending.value = { challenge, isPersistent };
      step.value = PhoneSignInStep.SecondFactor;
      return ResultExtensions.ok(undefined);
    }
    return result;
  },
}));

function pickDestination(value: string | null) {
  if (value) destination.value = value;
}

/** Sends the code to the destination the person picked: their own Telegram or one of the phone's trusted contacts. */
async function sendToDestination() {
  if (isSendingToDestination.value) return;
  isSendingToDestination.value = true;
  destinationFailure.value = null;
  const result = await send(phone.value, undefined, targetId.value);
  isSendingToDestination.value = false;
  if (!result.ok) {
    destinationFailure.value = result.failure;
    return;
  }
  codeForm.reset();
  isResent.value = false;
  resendFailure.value = null;
  step.value = telegramLinkUrl.value ? PhoneSignInStep.Telegram : PhoneSignInStep.Code;
}

/** Signs in as the account picked from the trusted contact's choice; a second factor of that account continues as at the code. */
async function chooseAccount(account: PhoneSignInAccountDto) {
  const current = choice.value;
  if (!current || choosing.value) return;
  choosing.value = account.userId;
  chooseFailure.value = null;
  const result = await client.choosePhoneSignInAccount(
    { token: current.token, userId: account.userId, isPersistent: current.isPersistent },
    { clientId: props.clientId },
  );
  choosing.value = null;
  if (result.ok) {
    emit('signedIn');
    return;
  }
  const challenge = readTwoFactorChallenge(result.failure);
  if (challenge?.userId && challenge.twoFactorTicket) {
    pending.value = { challenge, isPersistent: current.isPersistent };
    step.value = PhoneSignInStep.SecondFactor;
    return;
  }
  chooseFailure.value = result.failure;
}

/** Completes the sign-in with the second factor and the challenge's ticket, keeping the code step's `isPersistent`. */
async function completeSecondFactor(factor: SecondFactor, signal: AbortSignal) {
  const current = pending.value;
  const { userId, twoFactorTicket, twoFactorMethod } = current?.challenge ?? {};
  if (!current || !userId || !twoFactorTicket) return ResultExtensions.ok(undefined);
  const result = await client.signInWithSecondFactor(
    {
      userId,
      ticket: twoFactorTicket,
      ...(twoFactorMethod ? { twoFactorMethod } : {}),
      ...factor,
      isPersistent: current.isPersistent,
    },
    { clientId: props.clientId, signal },
  );
  if (result.ok) emit('signedIn');
  return result;
}

/** Sends a new code; a phone whose chat is no longer linked goes back to the Telegram step, which brings the code. */
async function resend() {
  isResending.value = true;
  isResent.value = false;
  resendFailure.value = null;
  const result = await send(phone.value, undefined, targetId.value);
  isResending.value = false;
  if (!result.ok) resendFailure.value = result.failure;
  else if (result.value.isCodeSent) isResent.value = true;
  else step.value = PhoneSignInStep.Telegram;
}

function useAnotherPhone() {
  step.value = PhoneSignInStep.Phone;
  telegramLinkUrl.value = null;
  pending.value = null;
  choice.value = null;
  targets.value = [];
  destination.value = ownDestination;
  sentToLabel.value = null;
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

  <form
    v-else-if="step === PhoneSignInStep.Target"
    class="flex flex-col gap-4"
    novalidate
    @submit.prevent="sendToDestination"
  >
    <RadioGroup
      name="destination"
      :model-value="destination"
      :legend="t('PhoneCodeSignIn.destination', 'Where should we send the code for {phone}?', { phone })"
      @update:model-value="pickDestination"
    >
      <RadioField :value="ownDestination" :label="t('PhoneCodeSignIn.ownTelegram', 'My Telegram')" />
      <RadioField
        v-for="target in targets"
        :key="target.id"
        :value="`${contactPrefix}${target.id}`"
        :label="target.label"
        :description="t('PhoneCodeSignIn.contactTelegram', 'Trusted contact: they get the code and tell it to you.')"
      />
    </RadioGroup>
    <FailureAlert :failure="destinationFailure" />
    <Button type="submit" is-full-width :is-loading="isSendingToDestination">
      {{ t('PhoneCodeSignIn.send', 'Send a code') }}
    </Button>
    <Button type="button" variant="ghost" tone="neutral" @click="useAnotherPhone">
      {{ t('PhoneCodeSignIn.otherPhone', 'Use another number') }}
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

  <SecondFactorForm
    v-else-if="step === PhoneSignInStep.SecondFactor && pending"
    :challenge="pending.challenge"
    :complete="completeSecondFactor"
    @restart="useAnotherPhone"
  />

  <div v-else-if="step === PhoneSignInStep.Account && choice" class="flex flex-col gap-4">
    <p class="text-sm text-muted-foreground">
      {{ t('PhoneCodeSignIn.chooseAccount', 'Choose the account to sign in to.') }}
    </p>
    <ul class="flex flex-col gap-2" :aria-label="t('PhoneCodeSignIn.accounts', 'Accounts')">
      <li v-for="account in choice.accounts" :key="account.userId">
        <Button
          type="button"
          variant="outline"
          tone="neutral"
          is-full-width
          class="h-auto justify-start gap-3 py-3"
          :is-loading="choosing === account.userId"
          :is-disabled="choosing !== null && choosing !== account.userId"
          @click="chooseAccount(account)"
        >
          <Avatar
            :src="account.pictureUrl ?? undefined"
            :name="account.displayName ?? undefined"
            :alt="account.displayName ?? ''"
            shape="circle"
            size="md"
            class="shrink-0"
          />
          <span class="min-w-0 flex-1 truncate text-left font-medium">
            {{ account.displayName || t('PhoneCodeSignIn.unnamedAccount', 'Unnamed account') }}
          </span>
          <Badge v-if="account.isSelf">{{ t('PhoneCodeSignIn.you', 'You') }}</Badge>
        </Button>
      </li>
    </ul>
    <FailureAlert :failure="chooseFailure" />
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
      <template v-else-if="sentToLabel">
        {{
          t('PhoneCodeSignIn.codeSentToContact', 'We sent a code to {contact} in Telegram. Ask them for it.', {
            contact: sentToLabel,
          })
        }}
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
