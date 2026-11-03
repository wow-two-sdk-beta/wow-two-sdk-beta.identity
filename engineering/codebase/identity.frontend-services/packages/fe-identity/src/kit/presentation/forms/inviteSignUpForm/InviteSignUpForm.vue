<script setup lang="ts">
import { fieldIssues, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type AppError } from '@wow-two-beta/fe/foundation/results';
import { boolean, object, type FieldIssue } from '@wow-two-beta/fe/foundation/validators';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Avatar } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple, InlineSpinner } from '@wow-two-beta/fe/presentation/feedback';
import {
  CheckboxField,
  Field,
  FilePicker,
  RadioField,
  RadioGroup,
  TextInput,
} from '@wow-two-beta/fe/presentation/forms';
import { computed, onScopeDispose, shallowRef, watch } from 'vue';
import {
  IdentityErrorCodeConstants,
  InviteSignUpStatus,
  InviteSignUpVerification,
  ProfileFieldMode,
  type InviteSignUpStartedDto,
  type SignInRuleDto,
  type SignUpProfileDto,
} from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { readIdentityCode } from '../../../account/AccountProblems';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { codeRule, inspectPicture, passwordRuleMessages } from '../../../account/validation';
import { FailureAlert } from '../../feedback/failureAlert';
import { inviteSignUpMessages } from './InviteSignUpMessages';
import { inviteSignUpFieldOf, inviteSignUpSchema } from './InviteSignUpSchema';
import { InviteSignUpStep } from './InviteSignUpStep';

/**
 * Signs a person up through an invitation link with no code typed: the name and picture the declaration's
 * `signUpProfile` asks for, then one "Verify phone in Telegram" action. It opens the bot, where sharing the contact
 * proves the phone and links the chat, and polls every 2 s until the service says the person joined, was refused or ran
 * out of time. A picture is checked before it is sent as the service checks it: its type, size and, where the browser
 * decodes pictures, its sides. A failure the client or the service finds in a field shows under that field.
 *
 * When the invitation names a trusted contact (`trustedContactHint`), the profile is followed by a choice: verify with
 * the person's own Telegram, the default, or with the contact. The contact receives a code in Telegram; it is entered
 * on the page and finishes the sign-up with no phone on the new account.
 */
defineOptions({ name: 'InviteSignUpForm' });

const props = withDefaults(
  defineProps<{
    /** The token of the invitation link the person signs up through. */
    invitationToken: string;
    /** What the sign-up asks for beside the phone, the declaration's `signUpProfile`. */
    profile: SignUpProfileDto;
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** The OIDC client the sign-up serves, recorded by the sign-in log. */
    clientId?: string | null;
    /** The invitation's trusted contact as a masked name, `InvitationPreviewDto.trustedContactHint`; the choice shows only with one. */
    trustedContactHint?: string | null;
    /** The rules the contact's code meets, the declared `code` field's `rules`; with none, it is only required. */
    codeRules?: ReadonlyArray<SignInRuleDto>;
    /** Whether to offer "Keep me signed in" on the code step, as the declaration's `isPersistenceOffered` says. */
    isPersistenceOffered?: boolean;
  }>(),
  {
    client: undefined,
    baseUrl: undefined,
    clientId: null,
    trustedContactHint: null,
    codeRules: undefined,
    isPersistenceOffered: false,
  },
);

const emit = defineEmits<{
  /** Fires once the person joined and the session cookie is set. */
  signedUp: [];
  /** Fires once the person joined but must sign in the usual way, such as with a second factor; no session is set. */
  signInRequired: [];
}>();

/** How long the page waits between two polls of the attempt. */
const pollIntervalMs = 2000;
/** The failures the page outlives by polling again: the network, a busy or failing service. */
const isTransient = (failure: ApiFailure) =>
  failure.code === 'transport' ||
  failure.code === 'timeout' ||
  (failure.code === 'http' && (failure.status >= 500 || failure.status === 429));
/** The outcomes another attempt cannot change. */
const finalOutcomes: ReadonlySet<string> = new Set([
  IdentityErrorCodeConstants.invitationExpired,
  IdentityErrorCodeConstants.invitationAccepted,
  IdentityErrorCodeConstants.invitationRevoked,
]);
/** The refusals of the contact's code after which it cannot be entered again: the final outcomes, and no attempt to finish. */
const codeEndings: ReadonlySet<string> = new Set([
  ...finalOutcomes,
  IdentityErrorCodeConstants.signUpVerificationNotFound,
]);

const client = useAccountClient(props);
const t = useAccountText();
const messages = inviteSignUpMessages(t, () => props.profile.pictureRules);
const step = shallowRef<InviteSignUpStep>(InviteSignUpStep.Profile);
/** The running or last attempt: the bot's link and the attempt's end. */
const attempt = shallowRef<InviteSignUpStartedDto | null>(null);
/** Why the last attempt ended, by its code. */
const outcome = shallowRef<string | null>(null);
const isSignedIn = shallowRef(false);
/** How the phone is verified, as the person picked it; the contact only where the invitation names one. */
const verification = shallowRef<InviteSignUpVerification>(InviteSignUpVerification.Telegram);
/** Whether the finished sign-up went through the trusted contact, which says so while the page moves on. */
const isByContact = shallowRef(false);
const isInspecting = shallowRef(false);
const previewUrl = shallowRef<string | undefined>(undefined);
let pollTimer: ReturnType<typeof setTimeout> | undefined;
let polling: AbortController | null = null;
let picks = 0;
let isDisposed = false;

const isNameAsked = computed(() => props.profile.displayName !== ProfileFieldMode.Off);
const isNameRequired = computed(() => props.profile.displayName === ProfileFieldMode.Required);
const isPictureAsked = computed(() => props.profile.picture !== ProfileFieldMode.Off);
const isPictureRequired = computed(() => props.profile.picture === ProfileFieldMode.Required);
/**
 * The file input's own attributes, which the picker passes through: exactly the declared types in `accept`, so a phone's
 * camera roll converts what it must, such as HEIC to JPEG.
 */
const pictureInput = computed(() => ({ name: 'picture', accept: props.profile.pictureRules.acceptedTypes.join(',') }));
const isAsked = (field: string) => (field === 'displayName' ? isNameAsked.value : isPictureAsked.value);

const waitingText = computed(() => t('InviteSignUpForm.waiting', 'Waiting for your contact…'));
const isContactChosen = computed(
  () => props.trustedContactHint !== null && verification.value === InviteSignUpVerification.TrustedContact,
);
/** What a finished sign-up says while the page moves on. */
const completedText = computed(() => {
  if (isByContact.value) return t('InviteSignUpForm.signedUpByContact', 'Your account is ready. Signing you in…');
  return isSignedIn.value
    ? t('InviteSignUpForm.signedUp', 'Your phone is verified. Signing you in…')
    : t('InviteSignUpForm.signInRequired', 'Your phone is verified. Sign in to finish.');
});

/** Follows `profile`, which the page may load after the form shows. */
const schema = computed(() => inviteSignUpSchema(props.profile, messages.field));

/** Puts a refusal of a field the form shows under that field; any other refusal stays the form's. */
function mapSubmitError(error: unknown): Record<string, ReadonlyArray<FieldIssue>> {
  const mapped: Record<string, ReadonlyArray<FieldIssue>> = { ...fieldIssues(error) };
  const code = readIdentityCode(error as AppError);
  const field = inviteSignUpFieldOf(code);
  if (code && field && isAsked(field)) mapped[field] = [{ message: messages.field(code) }];
  return mapped;
}

const form = useAccountForm(() => ({
  defaultValues: { displayName: '', picture: null as File | null },
  schema: schema.value,
  mapSubmitError,
  onSubmit: async (values, { signal }) => {
    const displayName = values.displayName.trim();
    const result = await client.startInviteSignUp(
      props.invitationToken,
      {
        ...(isNameAsked.value && displayName ? { displayName } : {}),
        ...(isPictureAsked.value && values.picture ? { picture: values.picture } : {}),
        ...(isContactChosen.value ? { verification: InviteSignUpVerification.TrustedContact } : {}),
      },
      { clientId: props.clientId, signal },
    );
    if (result.ok) begin(result.value);
    return result;
  },
}));

/** Follows `codeRules`, which the page may load after the form shows. */
const codeSchema = computed(() => object({ code: codeRule(props.codeRules), isPersistent: boolean() }));

/** Finishes the sign-up with the code the trusted contact received. */
const codeForm = useAccountForm(() => ({
  defaultValues: { code: '', isPersistent: false },
  schema: codeSchema.value,
  messages: passwordRuleMessages,
  onSubmit: async (values, { signal }) => {
    const result = await client.verifyInviteSignUp(
      { code: values.code.trim(), isPersistent: props.isPersistenceOffered && values.isPersistent },
      { clientId: props.clientId, signal },
    );
    if (result.ok) {
      isByContact.value = true;
      finish(true);
      return result;
    }
    // A wrong code stays on its field; a refusal no code can fix ends the attempt.
    const code = readIdentityCode(result.failure);
    if (code && codeEndings.has(code)) {
      end(code);
      return ResultExtensions.ok(undefined);
    }
    return result;
  },
}));

function pickVerification(value: string | null) {
  if (value === InviteSignUpVerification.Telegram || value === InviteSignUpVerification.TrustedContact) {
    verification.value = value;
  }
}

/** A local address of the picture for its preview; none where the browser gives none. */
function objectUrl(picture: Blob | null): string | undefined {
  try {
    return picture ? URL.createObjectURL(picture) : undefined;
  } catch {
    return undefined;
  }
}

function revokePreview() {
  if (previewUrl.value) URL.revokeObjectURL(previewUrl.value);
}

watch(
  () => form.values.picture,
  (picture) => {
    revokePreview();
    previewUrl.value = objectUrl(picture);
  },
);

/** Checks a picked picture as the service will, and takes it only when it fits; a misfit shows why instead. */
async function pickPicture(files: FileList | null) {
  const picture = files?.[0];
  if (!picture) return;
  const pick = ++picks;
  isInspecting.value = true;
  const issue = await inspectPicture(picture, props.profile.pictureRules);
  if (isDisposed || pick !== picks) return;
  isInspecting.value = false;
  form.setValue('picture', issue ? null : picture);
  if (issue) form.setFieldErrors({ picture: [messages.field(issue)] });
}

function removePicture() {
  picks += 1;
  isInspecting.value = false;
  form.setValue('picture', null);
}

function stopPolling() {
  clearTimeout(pollTimer);
  pollTimer = undefined;
  polling?.abort();
  polling = null;
}

/** Opens the bot in a new tab where the browser allows it; the waiting step's link opens it anyway. */
function openTelegram(url: string) {
  try {
    window.open(url, '_blank', 'noopener,noreferrer');
  } catch {
    // A blocked or missing window.open leaves the link to the person.
  }
}

function begin(started: InviteSignUpStartedDto) {
  stopPolling();
  attempt.value = started;
  outcome.value = null;
  if (started.isCodeSentToTrustedContact) {
    codeForm.reset();
    step.value = InviteSignUpStep.Code;
    return;
  }
  if (!started.telegramLinkUrl) {
    end(null);
    return;
  }
  step.value = InviteSignUpStep.Telegram;
  openTelegram(started.telegramLinkUrl);
  const deadline = Date.parse(started.expiresAt);
  polling = new AbortController();
  schedule(polling.signal, Number.isNaN(deadline) ? Number.POSITIVE_INFINITY : deadline);
}

/** Polls again in 2 s, or at the attempt's end when that comes sooner, so the last poll sees it. */
function schedule(signal: AbortSignal, deadline: number) {
  const wait = Math.max(0, Math.min(pollIntervalMs, deadline - Date.now()));
  pollTimer = setTimeout(() => void poll(signal, deadline), wait);
}

async function poll(signal: AbortSignal, deadline: number) {
  const result = await client.getInviteSignUpStatus({ clientId: props.clientId, signal });
  if (signal.aborted || isDisposed) return;
  if (!result.ok) {
    if (isTransient(result.failure) && Date.now() < deadline) schedule(signal, deadline);
    else end(readIdentityCode(result.failure));
    return;
  }
  const { status, failureCode } = result.value;
  if (status === InviteSignUpStatus.Completed) finish(result.value.isSignedIn);
  else if (status === InviteSignUpStatus.Failed) end(failureCode);
  else if (status === InviteSignUpStatus.Expired || Date.now() >= deadline) end(InviteSignUpStatus.Expired);
  else schedule(signal, deadline);
}

function end(code: string | null) {
  stopPolling();
  outcome.value = code;
  step.value = InviteSignUpStep.Ended;
}

function finish(signedIn: boolean) {
  stopPolling();
  isSignedIn.value = signedIn;
  step.value = InviteSignUpStep.Completed;
  if (signedIn) emit('signedUp');
  else emit('signInRequired');
}

/** Leaves the attempt and goes back to the profile, which keeps what the person gave. */
function startOver() {
  stopPolling();
  attempt.value = null;
  outcome.value = null;
  step.value = InviteSignUpStep.Profile;
}

onScopeDispose(() => {
  isDisposed = true;
  stopPolling();
  revokePreview();
});
</script>

<template>
  <form v-if="step === InviteSignUpStep.Profile" class="flex flex-col gap-4" novalidate @submit="form.handleSubmit">
    <form.Field v-if="isPictureAsked" v-slot="field" name="picture" :is-required="isPictureRequired">
      <Field :label="t('InviteSignUpForm.picture', 'Profile picture')" :helper="messages.pictureHint()">
        <div class="flex items-center gap-4">
          <Avatar
            :src="previewUrl"
            :name="form.values.displayName.trim() || undefined"
            :alt="t('InviteSignUpForm.picturePreview', 'Your picture')"
            shape="circle"
            size="2xl"
            class="shrink-0"
          />
          <div class="flex flex-col items-start gap-1">
            <FilePicker
              v-bind="pictureInput"
              :label="
                field.value
                  ? t('InviteSignUpForm.otherPicture', 'Choose another picture')
                  : t('InviteSignUpForm.choosePicture', 'Choose a picture')
              "
              @files-change="pickPicture"
            />
            <Button
              v-if="field.value && !isPictureRequired"
              type="button"
              variant="ghost"
              tone="neutral"
              size="sm"
              @click="removePicture"
            >
              {{ t('InviteSignUpForm.removePicture', 'Remove the picture') }}
            </Button>
          </div>
        </div>
      </Field>
    </form.Field>
    <form.Field v-if="isNameAsked" v-slot="field" name="displayName" :is-required="isNameRequired">
      <Field :label="t('InviteSignUpForm.displayName', 'Your name')">
        <TextInput v-model="field.value" name="displayName" autocomplete="name" @blur="field.onBlur" />
      </Field>
    </form.Field>
    <RadioGroup
      v-if="trustedContactHint"
      name="verification"
      :model-value="verification"
      :legend="t('InviteSignUpForm.verification', 'How should we verify you?')"
      @update:model-value="pickVerification"
    >
      <RadioField
        :value="InviteSignUpVerification.Telegram"
        :label="t('InviteSignUpForm.verifyOwn', 'With my own Telegram')"
        :description="
          t('InviteSignUpForm.verifyOwnHint', 'Open our bot, press Start and share your contact. No code to type.')
        "
      />
      <RadioField
        :value="InviteSignUpVerification.TrustedContact"
        :label="
          t('InviteSignUpForm.verifyContact', 'With my trusted contact, {contact}', { contact: trustedContactHint })
        "
      />
    </RadioGroup>
    <AlertSimple v-if="isContactChosen" severity="warning">
      {{
        t(
          'InviteSignUpForm.contactAccess',
          '{contact} gets a code in Telegram and tells it to you. Their codes give them full access to your account until you add a phone of your own and remove them.',
          { contact: trustedContactHint ?? '' },
        )
      }}
    </AlertSimple>
    <p v-else class="text-sm text-muted-foreground">
      {{
        t(
          'InviteSignUpForm.intro',
          'We check your phone number in Telegram: open our bot, press Start and share your contact. No code to type.',
        )
      }}
    </p>
    <FailureAlert :failure="form.state.submitError" />
    <Button type="submit" is-full-width :is-loading="form.state.isSubmitting" :is-disabled="isInspecting">
      {{
        isContactChosen
          ? t('InviteSignUpForm.sendToContact', 'Send the code to {contact}', { contact: trustedContactHint ?? '' })
          : t('InviteSignUpForm.verify', 'Verify phone in Telegram')
      }}
    </Button>
  </form>

  <form
    v-else-if="step === InviteSignUpStep.Code"
    class="flex flex-col gap-4"
    novalidate
    @submit="codeForm.handleSubmit"
  >
    <p class="text-sm text-muted-foreground">
      {{
        t('InviteSignUpForm.codeIntro', 'We sent a code to {contact} in Telegram. Ask them for it and enter it here.', {
          contact: trustedContactHint ?? '',
        })
      }}
    </p>
    <codeForm.Field v-slot="field" name="code" is-required>
      <Field :label="t('InviteSignUpForm.code', 'Code')">
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
        :label="t('InviteSignUpForm.keepSignedIn', 'Keep me signed in')"
        @blur="field.onBlur"
      />
    </codeForm.Field>
    <FailureAlert :failure="codeForm.state.submitError" />
    <Button type="submit" is-full-width :is-loading="codeForm.state.isSubmitting">
      {{ t('InviteSignUpForm.finish', 'Finish signing up') }}
    </Button>
    <Button type="button" variant="ghost" tone="neutral" @click="startOver">
      {{ t('InviteSignUpForm.startOver', 'Start over') }}
    </Button>
  </form>

  <div v-else-if="step === InviteSignUpStep.Telegram && attempt" class="flex flex-col gap-4">
    <p class="text-sm text-muted-foreground">
      {{
        t(
          'InviteSignUpForm.telegramIntro',
          'In Telegram, press Start and share your contact with the bot. This page continues by itself once it has your number.',
        )
      }}
    </p>
    <Button as-child is-full-width>
      <a :href="attempt.telegramLinkUrl ?? undefined" target="_blank" rel="noopener noreferrer">
        {{ t('InviteSignUpForm.openTelegram', 'Open Telegram') }}
      </a>
    </Button>
    <p role="status" class="flex justify-center text-sm text-muted-foreground">
      <InlineSpinner size="sm" tone="muted">
        {{ waitingText }}
      </InlineSpinner>
    </p>
    <Button type="button" variant="ghost" tone="neutral" @click="startOver">
      {{ t('InviteSignUpForm.startOver', 'Start over') }}
    </Button>
  </div>

  <div v-else-if="step === InviteSignUpStep.Ended" class="flex flex-col gap-4">
    <AlertSimple :severity="outcome === InviteSignUpStatus.Expired ? 'warning' : 'danger'">
      {{ messages.outcome(outcome) }}
    </AlertSimple>
    <Button v-if="!outcome || !finalOutcomes.has(outcome)" type="button" is-full-width @click="startOver">
      {{ t('InviteSignUpForm.tryAgain', 'Start again') }}
    </Button>
  </div>

  <AlertSimple v-else severity="success">
    {{ completedText }}
  </AlertSimple>
</template>
