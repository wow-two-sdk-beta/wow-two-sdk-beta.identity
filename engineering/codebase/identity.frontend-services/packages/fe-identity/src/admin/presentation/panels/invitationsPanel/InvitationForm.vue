<script setup lang="ts">
import { object, oneOf, phone as phoneRule } from '@wow-two-beta/fe/foundation/validators';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Field, PhoneInput } from '@wow-two-beta/fe/presentation/forms';
import { OrganizationRoleConstants, organizationRoles, type InvitationCreatedDto } from '../../../../contracts';
import { useAccountForm, useAccountText } from '../../../../kit/account/hooks';
import { FailureAlert } from '../../../../kit/presentation/feedback/failureAlert';
import type { OrganizationClient } from '../../../organizations';
import { RoleSelect } from '../../roleSelect';

/**
 * Makes an invitation link with any catalog role and, optionally, the one phone number that may redeem it; the server's
 * refusal shows above the button.
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
  }>(),
  { countries: undefined, defaultCountry: undefined },
);

const emit = defineEmits<{
  /** Fires with the new invitation and its link, which no later answer carries. */
  created: [created: InvitationCreatedDto];
}>();

const t = useAccountText();

const form = useAccountForm({
  defaultValues: { role: OrganizationRoleConstants.viewer as string, phoneNumber: '' },
  schema: object({
    role: oneOf(organizationRoles),
    phoneNumber: phoneRule({ countries: props.countries, isOptional: true }),
  }),
  onSubmit: async (values, { signal }) => {
    const result = await props.client.createInvitation(
      props.organizationId,
      { role: values.role, ...(values.phoneNumber ? { phoneNumber: values.phoneNumber } : {}) },
      signal,
    );
    if (result.ok) {
      form.reset();
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
    <FailureAlert :failure="form.state.submitError" />
    <Button type="submit" :is-loading="form.state.isSubmitting">
      {{ t('InvitationsPanel.create', 'Create the link') }}
    </Button>
  </form>
</template>
