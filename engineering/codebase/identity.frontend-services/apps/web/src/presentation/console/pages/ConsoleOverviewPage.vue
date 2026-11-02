<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { Badge, Card } from '@wow-two-beta/fe/presentation/display';
import { computed } from 'vue';
import { RouterLink } from 'vue-router';
import { ConsolePaths, useConsoleContext } from '../../../application/console';
import { isInstanceAdmin } from '../../../domain/console/ConsoleSession';

/**
 * Opens the console: the running versions, the organizations the person owns or administers with links to their
 * members and invitations (an instance admin also finds "Create organization" here), and the hosted pages.
 */
defineOptions({ name: 'ConsoleOverviewPage' });

const { session, status } = useConsoleContext();
const appVersion = __APP_VERSION__;
const organizations = computed(() => session.value?.organizations ?? []);
const isAdmin = computed(() => (session.value ? isInstanceAdmin(session.value) : false));
const pages = [
  { label: 'Sign in', href: HostedPageRouteConstants.signIn },
  { label: 'Sign up', href: HostedPageRouteConstants.signUp },
  { label: 'Reset a password', href: HostedPageRouteConstants.forgotPassword },
  { label: 'Account security', href: HostedPageRouteConstants.accountSecurity },
];
</script>

<template>
  <div class="flex flex-col gap-8">
    <header class="flex flex-wrap items-center gap-3">
      <h1 class="text-2xl font-semibold">Overview</h1>
      <div class="contents" aria-label="App version">
        <Badge>app {{ appVersion }}</Badge>
      </div>
      <div v-if="status" class="contents" aria-label="API version">
        <Badge>api {{ status.version }}</Badge>
      </div>
    </header>

    <section aria-labelledby="organizations-heading">
      <Card class="p-6">
        <div class="mb-4 flex flex-wrap items-center gap-3">
          <h2 id="organizations-heading" class="text-sm font-medium text-muted-foreground">Organizations</h2>
          <RouterLink
            v-if="isAdmin"
            :to="ConsolePaths.createOrganization()"
            class="ms-auto text-sm text-primary underline-offset-4 hover:underline"
          >
            Create organization
          </RouterLink>
        </div>
        <ul v-if="organizations.length" class="flex flex-col divide-y divide-border">
          <li
            v-for="organization in organizations"
            :key="organization.id"
            class="flex flex-wrap items-center gap-3 py-3 first:pt-0 last:pb-0"
          >
            <span class="font-medium">{{ organization.name }}</span>
            <Badge>{{ organization.role }}</Badge>
            <span class="ms-auto flex gap-4 text-sm">
              <RouterLink
                :to="ConsolePaths.members({ organizationId: organization.id })"
                class="text-primary underline-offset-4 hover:underline"
              >
                Members
              </RouterLink>
              <RouterLink
                :to="ConsolePaths.invitations({ organizationId: organization.id })"
                class="text-primary underline-offset-4 hover:underline"
              >
                Invitations
              </RouterLink>
            </span>
          </li>
        </ul>
        <p v-else class="text-sm text-muted-foreground">
          You own or administer no organization, so there is nobody to invite here{{ isAdmin ? ' yet' : '' }}.
        </p>
      </Card>
    </section>

    <section aria-labelledby="hosted-heading">
      <Card class="p-6">
        <h2 id="hosted-heading" class="text-sm font-medium text-muted-foreground">Hosted pages</h2>
        <ul class="mt-3 flex flex-col gap-2">
          <li v-for="page in pages" :key="page.href">
            <a :href="page.href" class="text-primary underline-offset-4 hover:underline">{{ page.label }}</a>
          </li>
        </ul>
      </Card>
    </section>
  </div>
</template>
