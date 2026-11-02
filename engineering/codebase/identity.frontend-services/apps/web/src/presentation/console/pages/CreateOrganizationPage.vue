<script setup lang="ts">
import { Card } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { computed } from 'vue';
import { useConsoleContext } from '../../../application/console';
import { isInstanceAdmin, type ConsoleOrganization } from '../../../domain/console/ConsoleSession';
import CreateOrganizationForm from '../components/CreateOrganizationForm.vue';

/**
 * Lets an instance admin create an organization, becoming its owner, then opens its members. Anyone else is told only an
 * instance admin creates organizations; the service refuses them too, reading the role live.
 */
defineOptions({ name: 'CreateOrganizationPage' });

const props = defineProps<{
  /** Opens the new organization's members. */
  onCreated: (organizationId: string) => void;
}>();

const { session, createOrganization } = useConsoleContext();
const isAdmin = computed(() => (session.value ? isInstanceAdmin(session.value) : false));

function created(organization: ConsoleOrganization) {
  props.onCreated(organization.id);
}
</script>

<template>
  <div class="flex max-w-2xl flex-col gap-6">
    <h1 class="text-2xl font-semibold">Create organization</h1>
    <Card v-if="isAdmin" class="p-6">
      <CreateOrganizationForm :create="createOrganization" @created="created" />
    </Card>
    <AlertSimple v-else severity="warning">Only an instance admin creates organizations.</AlertSimple>
  </div>
</template>
