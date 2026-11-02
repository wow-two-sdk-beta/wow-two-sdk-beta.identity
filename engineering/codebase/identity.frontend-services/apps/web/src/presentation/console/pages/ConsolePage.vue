<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { InvitationsPanel, MembersPanel } from '@wow-two-beta/fe-identity/admin';
import { Badge, Card } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { computed, shallowRef, watch } from 'vue';
import { useSignInOptions } from '../../../application/account/UseSignInOptions';
import { useConsoleSession } from '../../../application/console/UseConsoleSession';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';
import { SignInMethodId } from '../../../domain/account';

/**
 * Renders the console for a person holding an instance role: who is signed in, the running versions, the invitations and
 * members of each organization they own or administer, and the hosted pages this instance serves. Invitations are made
 * here, never from the host CLI. Nobody signed in goes to the sign-in page first; a person without an instance role is
 * told so.
 */
defineOptions({ name: 'ConsolePage' });

const props = defineProps<{
  /** Leaves for the sign-in page, which comes back here. */
  onSignInRequired: () => void;
}>();

const appVersion = __APP_VERSION__;
const { session, isDenied, failure } = useConsoleSession(() => props.onSignInRequired());
const { status } = useSystemStatus();
const { options } = useSignInOptions(null);
const pages = [
  { label: 'Sign in', href: HostedPageRouteConstants.signIn },
  { label: 'Sign up', href: HostedPageRouteConstants.signUp },
  { label: 'Reset a password', href: HostedPageRouteConstants.forgotPassword },
  { label: 'Account security', href: HostedPageRouteConstants.accountSecurity },
];

/** The organization the panels manage; the first one the person manages until they pick another. */
const organizationId = shallowRef<string | null>(null);
watch(session, (current) => {
  if (current && !current.organizations.some((organization) => organization.id === organizationId.value))
    organizationId.value = current.organizations[0]?.id ?? null;
});
const organization = computed(
  () => session.value?.organizations.find((candidate) => candidate.id === organizationId.value) ?? null,
);
/** The countries an invitation's phone may come from, as phone sign-in allows them. */
const countries = computed(() => {
  const phone = options.value?.methods.find((method) => method.id === SignInMethodId.PhoneCode);
  return phone && 'countries' in phone ? [...phone.countries] : undefined;
});
</script>

<template>
  <main class="mx-auto min-h-screen max-w-3xl px-6 py-16 font-sans text-foreground">
    <header class="flex flex-wrap items-center gap-3">
      <h1 class="text-3xl font-semibold">Identity console</h1>
      <div class="contents" aria-label="App version">
        <Badge>app {{ appVersion }}</Badge>
      </div>
      <div v-if="status" class="contents" aria-label="API version">
        <Badge>api {{ status.version }}</Badge>
      </div>
    </header>

    <AlertSimple v-if="isDenied" class="mt-8" severity="warning">
      This account has no console access. Sign in with an account that holds an instance role.
    </AlertSimple>
    <AlertSimple v-else-if="failure" class="mt-8" severity="danger">
      We could not load the console. Reload the page to try again.
    </AlertSimple>
    <template v-else-if="session">
      <p class="mt-4 text-sm text-muted-foreground" aria-label="Signed in">
        Signed in as {{ session.name }} · {{ session.roles.join(', ') }}
      </p>

      <section v-if="organization" class="mt-8 flex flex-col gap-6" aria-labelledby="organization-heading">
        <div class="flex flex-wrap items-center gap-3">
          <h2 id="organization-heading" class="text-xl font-semibold">{{ organization.name }}</h2>
          <Badge>{{ organization.role }}</Badge>
          <label v-if="session.organizations.length > 1" class="ml-auto text-sm text-muted-foreground">
            Organization
            <select v-model="organizationId" class="ml-2 rounded-md border bg-background px-2 py-1 text-foreground">
              <option v-for="candidate in session.organizations" :key="candidate.id" :value="candidate.id">
                {{ candidate.name }}
              </option>
            </select>
          </label>
        </div>
        <Card class="p-6">
          <h3 class="mb-4 text-sm font-medium text-muted-foreground">Invitations</h3>
          <InvitationsPanel
            :key="`invitations-${organization.id}`"
            :organization-id="organization.id"
            :countries="countries"
          />
        </Card>
        <Card class="p-6">
          <h3 class="mb-4 text-sm font-medium text-muted-foreground">Members</h3>
          <MembersPanel :key="`members-${organization.id}`" :organization-id="organization.id" />
        </Card>
      </section>
      <p v-else class="mt-8 text-sm text-muted-foreground">
        You own or administer no organization, so there is nobody to invite here.
      </p>

      <section class="mt-8" aria-labelledby="hosted-heading">
        <Card class="p-6">
          <h2 id="hosted-heading" class="text-sm font-medium text-muted-foreground">Hosted pages</h2>
          <ul class="mt-3 flex flex-col gap-2">
            <li v-for="page in pages" :key="page.href">
              <a :href="page.href" class="text-primary underline-offset-4 hover:underline">{{ page.label }}</a>
            </li>
          </ul>
        </Card>
      </section>
    </template>
  </main>
</template>
