<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { Badge, Card } from '@wow-two-beta/fe/presentation/display';
import { AlertSimple } from '@wow-two-beta/fe/presentation/feedback';
import { useConsoleSession } from '../../../application/console/UseConsoleSession';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';

/**
 * Renders the console for a person holding an instance role: who is signed in, the running versions and the hosted pages
 * this instance serves. Nobody signed in goes to the sign-in page first; a person without an instance role is told so.
 */
defineOptions({ name: 'ConsolePage' });

const props = defineProps<{
  /** Leaves for the sign-in page, which comes back here. */
  onSignInRequired: () => void;
}>();

const appVersion = __APP_VERSION__;
const { session, isDenied, failure } = useConsoleSession(() => props.onSignInRequired());
const { status } = useSystemStatus();
const pages = [
  { label: 'Sign in', href: HostedPageRouteConstants.signIn },
  { label: 'Sign up', href: HostedPageRouteConstants.signUp },
  { label: 'Reset a password', href: HostedPageRouteConstants.forgotPassword },
  { label: 'Account security', href: HostedPageRouteConstants.accountSecurity },
];
</script>

<template>
  <main class="mx-auto min-h-screen max-w-2xl px-6 py-16 font-sans text-foreground">
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
