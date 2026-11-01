<script setup lang="ts">
import { HostedPageRouteConstants } from '@wow-two-beta/fe-identity/contracts';
import { Badge, Card } from '@wow-two-beta/fe/presentation/display';
import { useSystemStatus } from '../../../application/system/UseSystemStatus';

/** Renders the console: the running versions and the hosted pages this instance serves. */
defineOptions({ name: 'ConsolePage' });

const appVersion = __APP_VERSION__;
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
  </main>
</template>
