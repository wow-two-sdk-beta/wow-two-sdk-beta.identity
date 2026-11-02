<script lang="ts">
/** Names an organization's console pages. */
export type OrganizationSection = 'members' | 'invitations';
</script>
<script setup lang="ts">
import { Badge } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import { ConsolePaths, useConsoleContext } from '../../../application/console';
import { findManagedOrganization, type ConsoleOrganization } from '../../../domain/console/ConsoleSession';

/**
 * Heads an organization's page: its name, the person's role there and tabs between its members and invitations, then
 * the page's panel. An organization the person does not own or administer, or no longer does, shows a notice instead.
 */
defineOptions({ name: 'OrganizationFrame' });
const props = defineProps<{
  /** The organization the route names. */
  organizationId: string;
  /** The page on screen. */
  section: OrganizationSection;
}>();
defineSlots<{
  /** The page's panel, for the organization the person manages. */
  default(scope: { organization: ConsoleOrganization }): unknown;
}>();

const { session } = useConsoleContext();
const organization = computed(() =>
  session.value ? findManagedOrganization(session.value, props.organizationId) : null,
);
const tabs = computed(() => [
  { section: 'members', label: 'Members', to: ConsolePaths.members({ organizationId: props.organizationId }) },
  {
    section: 'invitations',
    label: 'Invitations',
    to: ConsolePaths.invitations({ organizationId: props.organizationId }),
  },
]);
</script>

<template>
  <div v-if="organization" class="flex flex-col gap-6">
    <header class="flex flex-col gap-4">
      <div class="flex flex-wrap items-center gap-3">
        <h1 class="text-2xl font-semibold">{{ organization.name }}</h1>
        <Badge>{{ organization.role }}</Badge>
        <span class="text-sm text-muted-foreground">{{ organization.slug }}</span>
      </div>
      <nav :aria-label="`${organization.name} pages`" class="flex gap-1 border-b border-border">
        <RouterLink
          v-for="tab in tabs"
          :key="tab.section"
          :to="tab.to"
          :aria-current="tab.section === props.section ? 'page' : undefined"
          :class="[
            '-mb-px border-b-2 px-3 py-2 text-sm font-medium transition-colors',
            tab.section === props.section
              ? 'border-primary text-foreground'
              : 'border-transparent text-muted-foreground hover:text-foreground',
          ]"
        >
          {{ tab.label }}
        </RouterLink>
      </nav>
    </header>
    <slot :organization="organization"></slot>
  </div>
  <AlertSimple v-else severity="warning">
    You do not own or administer this organization.
    <RouterLink :to="ConsolePaths.overview()" class="underline underline-offset-4"> Back to the overview </RouterLink>
  </AlertSimple>
</template>
