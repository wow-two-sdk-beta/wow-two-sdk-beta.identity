<script setup lang="ts">
import { SidebarMenu, SidebarMenuGroup, SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import { computed, shallowRef, watch } from 'vue';
import { RouterLink } from 'vue-router';
import { ConsolePaths } from '../../../application/console';
import { isInstanceAdmin, type ConsoleSession } from '../../../domain/console/ConsoleSession';
import ConsoleIcon, { type ConsoleIconName } from './ConsoleIcon.vue';

/**
 * Lists the console's places: the overview, then under "Organizations" each organization the person owns or
 * administers, unfolding to its Members and Invitations, and for an instance admin "Create organization". The open
 * organization unfolds by itself; the others stay folded until picked.
 */
defineOptions({ name: 'ConsoleNav' });
const props = defineProps<{
  /** The signed-in person: their organizations and instance roles. */
  session: ConsoleSession;
  /** The current path, which marks the current place. */
  path: string;
}>();
const emit = defineEmits<{
  /** Fires when a place is picked, so a drawer can close. */
  navigate: [];
}>();

const isAdmin = computed(() => isInstanceAdmin(props.session));

/** The organizations shown unfolded: the one on screen, and any the person unfolded. */
const unfolded = shallowRef<ReadonlySet<string>>(new Set());
watch(
  () => props.path,
  (path) => {
    const open = props.session.organizations.find((organization) => belongsTo(path, organization.id));
    if (open && !unfolded.value.has(open.id)) unfolded.value = new Set([...unfolded.value, open.id]);
  },
  { immediate: true },
);

/** Whether `path` is one of the organization's pages. */
function belongsTo(path: string, organizationId: string): boolean {
  return path === ConsolePaths.members({ organizationId }) || path === ConsolePaths.invitations({ organizationId });
}

/** The places under an organization: its members and its invitations. */
function placesOf(organizationId: string): ReadonlyArray<{ label: string; to: string; icon: ConsoleIconName }> {
  return [
    { label: 'Members', to: ConsolePaths.members({ organizationId }), icon: 'members' },
    { label: 'Invitations', to: ConsolePaths.invitations({ organizationId }), icon: 'invitations' },
  ];
}

/** Folds or unfolds an organization. */
function setUnfolded(organizationId: string, isOpen: boolean) {
  const next = new Set(unfolded.value);
  if (isOpen) next.add(organizationId);
  else next.delete(organizationId);
  unfolded.value = next;
}
</script>

<template>
  <SidebarMenu class="min-h-0 flex-1 overflow-y-auto overflow-x-hidden">
    <SidebarMenuItem as-child :is-active="props.path === ConsolePaths.overview()">
      <template #icon>
        <ConsoleIcon name="overview" />
      </template>
      <RouterLink :to="ConsolePaths.overview()" @click="emit('navigate')">
        <span class="min-w-0 flex-1 truncate text-start">Overview</span>
      </RouterLink>
    </SidebarMenuItem>
    <SidebarMenuSection label="Organizations">
      <SidebarMenuGroup
        v-for="organization in props.session.organizations"
        :key="organization.id"
        :label="organization.name"
        :open="unfolded.has(organization.id)"
        @update:open="setUnfolded(organization.id, $event)"
      >
        <template #icon>
          <ConsoleIcon name="organization" />
        </template>
        <SidebarMenuItem
          v-for="place in placesOf(organization.id)"
          :key="place.to"
          as-child
          :is-active="props.path === place.to"
        >
          <template #icon>
            <ConsoleIcon :name="place.icon" />
          </template>
          <RouterLink :to="place.to" @click="emit('navigate')">
            <span class="min-w-0 flex-1 truncate text-start">{{ place.label }}</span>
          </RouterLink>
        </SidebarMenuItem>
      </SidebarMenuGroup>
      <li v-if="props.session.organizations.length === 0" class="px-2.5 py-1 text-xs text-muted-foreground">
        None yet
      </li>
      <SidebarMenuItem v-if="isAdmin" as-child :is-active="props.path === ConsolePaths.createOrganization()">
        <template #icon>
          <ConsoleIcon name="create" />
        </template>
        <RouterLink :to="ConsolePaths.createOrganization()" @click="emit('navigate')">
          <span class="min-w-0 flex-1 truncate text-start">Create organization</span>
        </RouterLink>
      </SidebarMenuItem>
    </SidebarMenuSection>
  </SidebarMenu>
</template>
