<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { EmailInput, Field, PasswordInput } from '@wow-two-beta/fe/presentation/forms';
import { computed, shallowRef } from 'vue';
import type { SignInRuleDto, SignedUpAccountDto } from '../../../../contracts';
import type { AccountClient } from '../../../account';
import { useAccountClient, useAccountForm, useAccountText } from '../../../account/hooks';
import { passwordRuleMessages } from '../../../account/validation';
import { FailureAlert } from '../../feedback/failureAlert';
import { signUpSchema } from './SignUpSchema';

/**
 * Creates an account with an email and password while sign-up is open, or with an invitation's token, then says what
 * comes next. An email that is not one, or a password that breaks `passwordRules`, shows under its field and sends nothing.
 */
defineOptions({ name: 'SignUpForm' });

const props = withDefaults(
  defineProps<{
    /** The account API to call; built from `baseUrl` when omitted. */
    client?: AccountClient;
    /** The service's origin; the consumer's own origin when omitted. */
    baseUrl?: string;
    /** Where the person signs in once the account exists; no link when omitted. */
    signInHref?: string;
    /** The rules a new password meets, the declaration's `passwordRules`; with none, the password is only required. */
    passwordRules?: ReadonlyArray<SignInRuleDto>;
    /**
     * The token of an invitation link: the account joins its organization with its role, and signs up even while sign-up
     * is by invitation only.
     */
    invitationToken?: string | null;
  }>(),
  { client: undefined, baseUrl: undefined, signInHref: undefined, passwordRules: undefined, invitationToken: null },
);

const emit = defineEmits<{
  /** Fires with the account a sign-up created. */
  signedUp: [account: SignedUpAccountDto];
}>();

const client = useAccountClient(props);
const t = useAccountText();
const created = shallowRef<{ account: SignedUpAccountDto; email: string } | null>(null);

/** Follows `passwordRules`, which the page may load after the form shows. */
const schema = computed(() => signUpSchema(props.passwordRules));

const form = useAccountForm(() => ({
  defaultValues: { email: '', password: '' },
  schema: schema.value,
  validateOn: 'blur' as const,
  messages: passwordRuleMessages,
  onSubmit: async (values, { signal }) => {
    const request = props.invitationToken ? { ...values, invitationToken: props.invitationToken } : values;
    const result = await client.signUp(request, signal);
    if (result.ok) {
      created.value = { account: result.value, email: values.email };
      emit('signedUp', result.value);
    }
    return result;
  },
}));
</script>

<template>
  <div v-if="created" class="flex flex-col gap-4">
    <AlertSimple severity="success">
      <template v-if="created.account.isConfirmationSent">
        {{
          t('SignUpForm.confirmationSent', 'Check your inbox: we sent a link to {email} to confirm the address.', {
            email: created.email,
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
