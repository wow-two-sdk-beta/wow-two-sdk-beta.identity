<script lang="ts">
/** @internal The sidebar's width, which the brand above it shares. */
const SidebarWidth = '248px';
</script>
<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple, Spinner } from '@wow-two-beta/fe/presentation/feedback';
import {
  AppShell,
  AppShellContent,
  AppShellHeader,
  AppShellMain,
  AppShellSidebar,
} from '@wow-two-beta/fe/presentation/layout';
import { AccountMenu, MenuItem, type AccountMenuVersion } from '@wow-two-beta/fe/presentation/nav';
import { computed, shallowRef, watch } from 'vue';
import { RouterLink, RouterView } from 'vue-router';
import { useSignInOptions } from '../../../application/account';
import { ConsolePaths, provideConsoleContext, useConsoleSession } from '../../../application/console';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';
import ConsoleBrand from '../components/ConsoleBrand.vue';
import ConsoleIcon from '../components/ConsoleIcon.vue';
import ConsoleNav from '../components/ConsoleNav.vue';

/**
 * Frames the console, nothing but a loading state until the session resolves; nobody signed in goes to the sign-in page
 * first and comes back, and a person with neither an instance role nor a managed organization sees only that they have
 * no access, with a way to sign out. Everyone else gets the shell: a top bar with the brand, the signed-in person (name
 * and instance roles) and the account menu (versions, appearance, account security, sign out); a sidebar with the
 * overview and each managed organization's members and invitations, which folds into a drawer below `lg`; and the page.
 * Every move between pages reads the session again, so a role granted or revoked since shows without a new sign-in.
 */
defineOptions({ name: 'ConsoleLayout' });

const props = defineProps<{
  /** The current path: marks the current place and reloads the session when it changes. */
  path: string;
  /** Leaves for the sign-in page, which comes back here. */
  onSignInRequired: () => void;
  /** Leaves for the sign-in page once the person signed out. */
  onSignedOut: () => void;
  /** Opens another page of the app, such as the account security page. */
  onNavigate: (path: string) => void;
}>();

const appVersion = __APP_VERSION__;
const { session, isLoading, isDenied, failure, refresh, createOrganization, signOut, isSigningOut, signOutFailure } =
  useConsoleSession({
    onSignInRequired: () => props.onSignInRequired(),
    onSignedOut: () => props.onSignedOut(),
  });
const { status } = useSystemStatus();
const { options } = useSignInOptions(null);
provideConsoleContext({ session, options, status, createOrganization });

/** Whether the navigation drawer is open, below `lg`. */
const isNavOpen = shallowRef(false);

watch(
  () => props.path,
  () => {
    isNavOpen.value = false;
    void refresh();
  },
);

/** Opens the hosted account security page: password, sessions and linked sign-ins. */
function openAccountSecurity() {
  props.onNavigate(HostedPageRouteConstants.accountSecurity);
}

const brandName = computed(() => options.value?.brandName || 'Identity');

/** The account menu's second line: the person's instance roles, when they hold any. */
const rolesLine = computed(() => session.value?.roles.join(', ') || undefined);

/** The console's and the service's versions, the service's build commit apart. */
const versions = computed<ReadonlyArray<AccountMenuVersion>>(() => {
  const [api, commit] = (status.value?.version ?? '…').split('+');
  return [
    { label: 'Console', version: appVersion },
    { label: 'API', version: api || '…', ...(commit ? { commit } : {}) },
  ];
});
</script>

<template>
  <div v-if="isLoading" class="grid min-h-dvh place-items-center px-4 font-sans text-foreground">
    <p class="flex items-center gap-2 text-muted-foreground" role="status" aria-label="Loading">
      <Spinner /> Loading the console…
    </p>
  </div>
  <main
    v-else-if="isDenied"
    class="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center gap-4 px-4 py-10 font-sans text-foreground"
  >
    <AlertSimple severity="warning">
      This account has no console access. Sign in with an account that holds an instance role or manages an
      organization.
    </AlertSimple>
    <AlertSimple v-if="signOutFailure" severity="danger">We could not sign you out. Try again.</AlertSimple>
    <Button variant="outline" tone="neutral" :is-loading="isSigningOut" @click="signOut">Sign out</Button>
  </main>
  <main v-else-if="failure" class="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center px-4 font-sans">
    <AlertSimple severity="danger">We could not load the console. Reload the page to try again.</AlertSimple>
  </main>
  <AppShell
    v-else-if="session"
    class="surface-ambient font-sans"
    sidebar-breakpoint="lg"
    :sidebar-width="SidebarWidth"
    :sidebar-open="isNavOpen"
    @update:sidebar-open="isNavOpen = $event"
  >
    <AppShellHeader height="auto" variant="glass" class="p-0">
      <div class="flex w-full min-w-0 items-stretch">
        <!-- From `lg` up, the brand sits above the sidebar, as wide as it. -->
        <div class="hidden shrink-0 items-center border-r border-border px-4 lg:flex" :style="{ width: SidebarWidth }">
          <RouterLink :to="ConsolePaths.overview()" class="min-w-0" aria-label="Console overview">
            <ConsoleBrand :brand-name="brandName" />
          </RouterLink>
        </div>
        <div class="flex min-h-14 min-w-0 flex-1 items-center gap-2 px-4 py-2 lg:px-6">
          <Button
            variant="ghost"
            tone="neutral"
            size="md"
            shape="square"
            class="shrink-0 lg:hidden"
            aria-label="Open navigation"
            :aria-expanded="isNavOpen"
            @click="isNavOpen = true"
          >
            <ConsoleIcon name="menu" :size="18" />
          </Button>
          <RouterLink :to="ConsolePaths.overview()" class="min-w-0 lg:hidden" aria-label="Console overview">
            <ConsoleBrand :brand-name="brandName" />
          </RouterLink>
          <div class="ms-auto flex min-w-0 items-center gap-3">
            <div class="hidden min-w-0 flex-col items-end gap-0.5 sm:flex" aria-label="Signed in">
              <span class="max-w-56 truncate text-sm font-medium text-foreground">{{ session.name }}</span>
              <span v-if="session.roles.length" class="flex gap-1" aria-label="Instance roles">
                <Badge v-for="role in session.roles" :key="role" size="sm">{{ role }}</Badge>
              </span>
            </div>
            <AccountMenu
              :name="session.name"
              :description="rolesLine"
              :versions="versions"
              :is-busy="isSigningOut"
              @sign-out="signOut"
            >
              <template #items>
                <MenuItem @select="openAccountSecurity"> Account security </MenuItem>
              </template>
            </AccountMenu>
          </div>
        </div>
      </div>
    </AppShellHeader>
    <AppShellSidebar variant="glass">
      <ConsoleNav :session="session" :path="props.path" @navigate="isNavOpen = false" />
    </AppShellSidebar>
    <AppShellMain>
      <AppShellContent gutter="lg" max-width="xl">
        <AlertSimple v-if="signOutFailure" severity="danger" class="mb-6">
          We could not sign you out. Try again.
        </AlertSimple>
        <RouterView />
      </AppShellContent>
    </AppShellMain>
  </AppShell>
</template>
