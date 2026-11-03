<script setup lang="ts">
import { object, oneOf, phone as phoneRule } from '@wow-two-beta/fe/foundation/validators';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { Field, PhoneInput } from '@wow-two-beta/fe/presentation/forms';
import { computed, onMounted, onScopeDispose, shallowRef } from 'vue';
import {
  OrganizationRoleConstants,
  organizationRoles,
  type InvitationCreatedDto,
  type OrganizationMemberDto,
} from '../../../../contracts';
import { useAccountForm, useAccountText } from '../../../../kit/account/hooks';
import { FailureAlert } from '../../../../kit/presentation/feedback/failureAlert';
import type { OrganizationClient } from '../../../organizations';
import { memberName } from '../../../organizations/OrganizationLabels';
import { RoleSelect } from '../../roleSelect';
import { TrustedContactSelect, type TrustedContactOption } from '../../trustedContactSelect';

/**
 * Makes an invitation link with any catalog role and, optionally, the one phone number that may redeem it and a trusted
 * contact who receives the invited person's sign-in codes: the signed-in person ("Me"), or another member whose phone is
 * confirmed. A contact is full access to the new account, which the form says beside the picker. The server's refusal
 * shows above the button.
 */
defineOptions({ name: 'InvitationForm' });

const props = withDefaults(
  defineProps<{
    /** The organization the invitation is into. */
    organizationId: string;
    /** The organization API to call. */
    client: OrganizationClient;
    /** The ISO codes of the countries the phone may come from, in display order; every country when omitted. */
    countries?: ReadonlyArray<string>;
    /** The ISO code selected first; the first allowed country when omitted. */
    defaultCountry?: string;
    /** The signed-in person's id, who the picker offers as "Me" while their own phone is confirmed; no "Me" without it. */
    currentUserId?: string | null;
  }>(),
  { countries: undefined, defaultCountry: undefined, currentUserId: null },
);

const emit = defineEmits<{
  /** Fires with the new invitation and its link, which no later answer carries. */
  created: [created: InvitationCreatedDto];
}>();

const t = useAccountText();
/** The picker's value for no trusted contact. */
const noContact = '__none__';
const members = shallowRef<ReadonlyArray<OrganizationMemberDto>>([]);
const trustedContact = shallowRef(noContact);
const controller = new AbortController();

/** Reads the members to offer as contacts; a failure leaves the picker to what it knows, as the panel lists no members. */
onMounted(async () => {
  const result = await props.client.listMembers(props.organizationId, controller.signal);
  if (!controller.signal.aborted && result.ok) members.value = result.value;
});
onScopeDispose(() => controller.abort());

/** The picker's choices: none, "Me" while the signed-in person's phone is confirmed, then the other members with one. */
const contactOptions = computed<ReadonlyArray<TrustedContactOption>>(() => {
  const current = props.currentUserId;
  const own = current ? members.value.find((member) => member.userId === current) : undefined;
  const choices: TrustedContactOption[] = [];
  if (current && (!own || own.isPhoneConfirmed))
    choices.push({ value: current, label: t('InvitationsPanel.trustedMe', 'Me') });
  for (const member of members.value) {
    if (member.isPhoneConfirmed && member.userId !== current) {
      choices.push({ value: member.userId, label: memberName(member) });
    }
  }
  return choices.length > 0
    ? [{ value: noContact, label: t('InvitationsPanel.trustedNone', 'No trusted contact') }, ...choices]
    : [];
});
const isContactPicked = computed(() => trustedContact.value !== noContact);

const form = useAccountForm({
  defaultValues: { role: OrganizationRoleConstants.viewer as string, phoneNumber: '' },
  schema: object({
    role: oneOf(organizationRoles),
    phoneNumber: phoneRule({ countries: props.countries, isOptional: true }),
  }),
  onSubmit: async (values, { signal }) => {
    const result = await props.client.createInvitation(
      props.organizationId,
      {
        role: values.role,
        ...(values.phoneNumber ? { phoneNumber: values.phoneNumber } : {}),
        ...(isContactPicked.value ? { trustedContactUserId: trustedContact.value } : {}),
      },
      signal,
    );
    if (result.ok) {
      form.reset();
      trustedContact.value = noContact;
      emit('created', result.value);
    }
    return result;
  },
});
</script>

<template>
  <form class="flex flex-col gap-4" novalidate @submit="form.handleSubmit">
    <div class="grid gap-4 sm:grid-cols-[10rem_1fr]">
      <form.Field v-slot="field" name="role" is-required>
        <Field :label="t('InvitationsPanel.role', 'Role')">
          <RoleSelect
            v-model="field.value"
            name="role"
            :label="t('InvitationsPanel.role', 'Role')"
            :is-disabled="form.state.isSubmitting"
          />
        </Field>
      </form.Field>
      <form.Field v-slot="field" name="phoneNumber">
        <Field
          :label="t('InvitationsPanel.phone', 'Phone number')"
          :helper="t('InvitationsPanel.phoneHelper', 'Optional: only this number may redeem the link.')"
        >
          <PhoneInput
            v-model="field.value"
            name="phoneNumber"
            class="w-full [&>span]:shrink-0 [&>span]:whitespace-nowrap"
            :countries="countries"
            :default-country="defaultCountry"
            @blur="field.onBlur"
          />
        </Field>
      </form.Field>
    </div>
    <Field
      v-if="contactOptions.length"
      :label="t('InvitationsPanel.trustedContact', 'Trusted contact')"
      :helper="
        t(
          'InvitationsPanel.trustedContactHelper',
          'Optional: receives the invited person’s sign-in codes while they have no phone of their own.',
        )
      "
    >
      <TrustedContactSelect
        v-model="trustedContact"
        name="trustedContactUserId"
        :options="contactOptions"
        :label="t('InvitationsPanel.trustedContact', 'Trusted contact')"
        :is-disabled="form.state.isSubmitting"
      />
    </Field>
    <AlertSimple v-if="isContactPicked" severity="warning">
      {{
        t(
          'InvitationsPanel.trustedContactWarning',
          'A trusted contact receives this account’s sign-in codes. That gives them full access to the account, the same as its own phone.',
        )
      }}
    </AlertSimple>
    <FailureAlert :failure="form.state.submitError" />
    <Button type="submit" :is-loading="form.state.isSubmitting">
      {{ t('InvitationsPanel.create', 'Create the link') }}
    </Button>
  </form>
</template>
