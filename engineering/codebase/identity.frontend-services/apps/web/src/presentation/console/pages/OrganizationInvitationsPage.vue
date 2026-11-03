<script setup lang="ts">
import { InvitationsPanel } from '@wow-two-beta/fe-identity/admin';
import { Card } from '@wow-two-beta/fe/presentation/display';
import { computed } from 'vue';
import { useConsoleContext } from '../../../application/console';
import { SignInMethodId } from '../../../domain/account';
import OrganizationFrame from '../components/OrganizationFrame.vue';

/**
 * Manages an organization's invitations for its owners and admins, through fe-identity's panel: an invited phone comes
 * from the countries phone sign-in allows, and the signed-in person may name themselves, or another member with a
 * confirmed phone, the invited person's trusted contact. Invitations are made here, never from the host CLI.
 */
defineOptions({ name: 'OrganizationInvitationsPage' });

const props = defineProps<{
  /** The organization the route names. */
  organizationId: string;
}>();

const { options, session } = useConsoleContext();
/** The countries an invitation's phone may come from, as phone sign-in allows them. */
const countries = computed(() => {
  const phone = options.value?.methods.find((method) => method.id === SignInMethodId.PhoneCode);
  return phone && 'countries' in phone ? [...phone.countries] : undefined;
});
</script>

<template>
  <OrganizationFrame v-slot="{ organization }" :organization-id="props.organizationId" section="invitations">
    <Card class="p-6">
      <InvitationsPanel
        :key="organization.id"
        :organization-id="organization.id"
        :countries="countries"
        :current-user-id="session?.userId"
      />
    </Card>
  </OrganizationFrame>
</template>
