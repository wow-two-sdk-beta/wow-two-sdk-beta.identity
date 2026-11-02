<script setup lang="ts">
import { MembersPanel } from '@wow-two-beta/fe-identity/admin';
import { Card } from '@wow-two-beta/fe/presentation/display';
import OrganizationFrame from '../components/OrganizationFrame.vue';

/** Manages an organization's members for its owners and admins: roles and removal, through fe-identity's panel. */
defineOptions({ name: 'OrganizationMembersPage' });

const props = defineProps<{
  /** The organization the route names. */
  organizationId: string;
}>();
</script>

<template>
  <OrganizationFrame v-slot="{ organization }" :organization-id="props.organizationId" section="members">
    <Card class="p-6">
      <MembersPanel :key="organization.id" :organization-id="organization.id" />
    </Card>
  </OrganizationFrame>
</template>
