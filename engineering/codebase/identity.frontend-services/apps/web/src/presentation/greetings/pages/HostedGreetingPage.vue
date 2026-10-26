<script setup lang="ts">
import { GreetingWidget } from '@wow-two-beta/fe-identity/kit';
import type { HostedGreetingRequest } from '../../../domain/greetings/HostedGreetingRequest';

/** Renders the hosted greeting page an app forwards to, and the way back to that app. */
defineOptions({ name: 'HostedGreetingPage' });

defineProps<{
  /** What the forwarding app asked for, decoded from the route. */
  request: HostedGreetingRequest;
}>();
</script>

<template>
  <main class="mx-auto min-h-screen max-w-xl px-6 py-16 font-sans text-foreground">
    <h1 class="text-3xl font-semibold">Greetings</h1>
    <section class="mt-8" aria-label="Greeting">
      <GreetingWidget :initial-name="request.name" />
    </section>
    <a v-if="request.returnUrl" :href="request.returnUrl" class="mt-6 inline-block underline">Back to the app</a>
  </main>
</template>
