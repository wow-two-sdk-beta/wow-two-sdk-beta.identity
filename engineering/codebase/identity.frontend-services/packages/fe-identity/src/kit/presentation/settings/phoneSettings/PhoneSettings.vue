<script setup lang="ts">
import type { AppError } from '@wow-two-beta/fe/foundation/results';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple, InlineSpinner } from '@wow-two-beta/fe/presentation/feedback';
import { onMounted, onScopeDispose, shallowRef } from 'vue';
import { IdentityErrorCodeConstants, type AccountPhoneDto } from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { isIdentityCode } from '../../../account/AccountProblems';
import { useAccountClient, useAccountText } from '../../../account/hooks';
import { FailureAlert } from '../../feedback/failureAlert';

/**
 * Shows the signed-in account's own phone and adds or changes it through the Telegram bot: the bot's `add-phone` link
 * opens, sharing the contact there confirms the number, and the block polls the account's phone every 2 s until it shows
 * the new number or 5 minutes pass. An account with no phone adds one from any session; changing one needs a session
 * signed in with the own phone (`canChange`), else the block says so. A changed number ends every session, this one's
 * included, so the wait ends there too: the block says to sign in again and fires `signedOut`. The block renders nothing
 * while the service has no phone route (`404`).
 */
defineOptions({ name: 'PhoneSettings' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
  }>(),
  { client: undefined, baseUrl: undefined },
);

const emit = defineEmits<{
  /** Fires once the account's phone is saved, with the phone as the service now shows it. */
  changed: [phone: AccountPhoneDto];
  /** Fires once the session ended during the wait, as a changed number ends it. */
  signedOut: [];
}>();

/** How long the block waits between two polls of the phone. */
const pollIntervalMs = 2000;
/** How long the block waits for the contact before it gives up; the bot link carries no end of its own. */
const pollWindowMs = 5 * 60 * 1000;

const client = useAccountClient(props);
const t = useAccountText();
const phone = shallowRef<AccountPhoneDto | null>(null);
const failure = shallowRef<AppError | null>(null);
const isAbsent = shallowRef(false);
const isStarting = shallowRef(false);
const startFailure = shallowRef<AppError | null>(null);
/** The bot link while the block waits for the contact. */
const link = shallowRef<string | null>(null);
const isSaved = shallowRef(false);
const isTimedOut = shallowRef(false);
const isSignedOut = shallowRef(false);
const controller = new AbortController();
let pollTimer: ReturnType<typeof setTimeout> | undefined;
let isDisposed = false;

async function load() {
  const result = await client.getPhone(controller.signal);
  if (controller.signal.aborted) return;
  if (result.ok) phone.value = result.value;
  else if (result.failure.status === 404) isAbsent.value = true;
  else failure.value = result.failure;
}

onMounted(() => void load());

function stopWaiting() {
  clearTimeout(pollTimer);
  pollTimer = undefined;
  link.value = null;
}

/** Opens the bot in a new tab where the browser allows it; the waiting state's link opens it anyway. */
function openTelegram(url: string) {
  try {
    window.open(url, '_blank', 'noopener,noreferrer');
  } catch {
    // A blocked or missing window.open leaves the link to the person.
  }
}

/** Starts the add or the change: opens the bot link, then waits for the number to show. */
async function start() {
  if (isStarting.value) return;
  isStarting.value = true;
  startFailure.value = null;
  isSaved.value = false;
  isTimedOut.value = false;
  const result = await client.createPhoneTelegramLink(controller.signal);
  isStarting.value = false;
  if (controller.signal.aborted) return;
  if (!result.ok) {
    startFailure.value = result.failure;
    return;
  }
  const before = phone.value;
  link.value = result.value.telegramLinkUrl;
  openTelegram(result.value.telegramLinkUrl);
  schedule(before, Date.now() + pollWindowMs);
}

function schedule(before: AccountPhoneDto | null, deadline: number) {
  pollTimer = setTimeout(() => void poll(before, deadline), pollIntervalMs);
}

/** Whether `now` shows a number that is not the one `before` showed: a first phone, or another number. */
function isNewPhone(before: AccountPhoneDto | null, now: AccountPhoneDto): boolean {
  return now.isConfirmed && (!before?.isConfirmed || now.phoneHint !== before.phoneHint);
}

async function poll(before: AccountPhoneDto | null, deadline: number) {
  const result = await client.getPhone(controller.signal);
  if (isDisposed || controller.signal.aborted) return;
  if (result.ok && isNewPhone(before, result.value)) {
    stopWaiting();
    phone.value = result.value;
    isSaved.value = true;
    emit('changed', result.value);
    return;
  }
  if (!result.ok && result.failure.status === 401) {
    stopWaiting();
    isSignedOut.value = true;
    emit('signedOut');
    return;
  }
  if (Date.now() >= deadline) {
    stopWaiting();
    isTimedOut.value = true;
    return;
  }
  schedule(before, deadline);
}

function cancel() {
  stopWaiting();
}

onScopeDispose(() => {
  isDisposed = true;
  stopWaiting();
  controller.abort();
});
</script>

<template>
  <section v-if="!isAbsent" class="flex flex-col gap-3" :aria-label="t('PhoneSettings.title', 'Phone')">
    <h2 class="text-lg font-semibold">{{ t('PhoneSettings.title', 'Phone') }}</h2>
    <FailureAlert :failure="failure" />
    <template v-if="phone">
      <p class="text-sm text-muted-foreground">
        <template v-if="phone.isConfirmed">
          {{ t('PhoneSettings.current', 'Your phone: {phone}', { phone: phone.phoneHint ?? '' }) }}
        </template>
        <template v-else>
          {{ t('PhoneSettings.none', 'You have no phone of your own yet.') }}
        </template>
      </p>
      <AlertSimple v-if="isSaved" severity="success">
        {{ t('PhoneSettings.saved', 'Your phone is saved.') }}
      </AlertSimple>
      <AlertSimple v-if="isTimedOut" severity="warning">
        {{ t('PhoneSettings.timedOut', 'We did not get your contact in time. Try again.') }}
      </AlertSimple>
      <AlertSimple v-if="isSignedOut" severity="warning">
        {{ t('PhoneSettings.signedOut', 'A changed phone ends every session. Sign in again with your phone.') }}
      </AlertSimple>
      <div v-if="link" class="flex flex-col gap-3">
        <p class="text-sm text-muted-foreground">
          {{
            t(
              'PhoneSettings.telegramIntro',
              'In Telegram, press Start and share your contact with the bot. This page updates once it has your number.',
            )
          }}
        </p>
        <Button as-child is-full-width>
          <a :href="link" target="_blank" rel="noopener noreferrer">
            {{ t('PhoneSettings.openTelegram', 'Open Telegram') }}
          </a>
        </Button>
        <p role="status" class="flex justify-center text-sm text-muted-foreground">
          <InlineSpinner size="sm" tone="muted">
            {{ t('PhoneSettings.waiting', 'Waiting for your contact…') }}
          </InlineSpinner>
        </p>
        <Button type="button" variant="ghost" tone="neutral" @click="cancel">
          {{ t('PhoneSettings.cancel', 'Cancel') }}
        </Button>
      </div>
      <template v-else-if="!isSignedOut">
        <div v-if="phone.canChange">
          <Button type="button" variant="outline" tone="neutral" :is-loading="isStarting" @click="start">
            {{ phone.isConfirmed ? t('PhoneSettings.change', 'Change phone') : t('PhoneSettings.add', 'Add a phone') }}
          </Button>
        </div>
        <p v-else class="text-sm text-muted-foreground">
          {{ t('PhoneSettings.cannotChange', 'To change your phone, sign in with the phone you have now.') }}
        </p>
      </template>
      <AlertSimple v-if="isIdentityCode(startFailure, IdentityErrorCodeConstants.mainPhoneRequired)" severity="warning">
        {{ t('PhoneSettings.mainPhoneRequired', 'To change your phone, sign in with the phone you have now.') }}
      </AlertSimple>
      <FailureAlert v-else :failure="startFailure" />
    </template>
  </section>
</template>
