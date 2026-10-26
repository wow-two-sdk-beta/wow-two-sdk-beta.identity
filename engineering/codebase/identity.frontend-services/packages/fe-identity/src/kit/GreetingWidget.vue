<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Card } from '@wow-two-beta/fe/presentation/display';
import { onScopeDispose, shallowRef } from 'vue';
import { GreetingConstants, type GreetingModel } from '../contracts';
import { createGreetingsClient, type GreetingsClient } from './CreateGreetingsClient';

/** Greets a name through the Identity contract — on the consumer's backend or on the running service. */
defineOptions({ name: 'GreetingWidget' });

const props = withDefaults(
  defineProps<{
    /** The contract endpoint to call; the consumer's own origin when omitted. */
    client?: GreetingsClient;
    /** The name the field starts with. */
    initialName?: string;
  }>(),
  { client: undefined, initialName: '' },
);

const emit = defineEmits<{
  /** Fires with each greeting served. */
  greeted: [greeting: GreetingModel];
}>();

const client = props.client ?? createGreetingsClient();
const name = shallowRef(props.initialName);
const message = shallowRef<string | null>(null);
const error = shallowRef<string | null>(null);
const pending = shallowRef(false);
let controller: AbortController | null = null;

async function greet() {
  controller?.abort();
  const current = new AbortController();
  controller = current;
  pending.value = true;
  error.value = null;

  const result = await client.greet(name.value, current.signal);
  if (current.signal.aborted) return;
  pending.value = false;
  if (result.ok) {
    message.value = result.value.message;
    emit('greeted', result.value);
  } else {
    message.value = null;
    error.value = result.failure.status === 400 ? 'That name is not accepted.' : result.failure.message;
  }
}

onScopeDispose(() => controller?.abort());
</script>

<template>
  <Card class="p-6">
    <form class="flex flex-wrap items-end gap-3" @submit.prevent="greet">
      <label class="flex min-w-48 flex-1 flex-col gap-1 text-sm">
        <span class="font-medium text-muted-foreground">Name</span>
        <input
          v-model="name"
          name="name"
          autocomplete="off"
          :maxlength="GreetingConstants.maxNameLength"
          class="rounded-md border border-border bg-background px-3 py-2"
        />
      </label>
      <Button type="submit" :is-loading="pending">Greet</Button>
    </form>
    <p v-if="error" class="mt-4 text-destructive" role="alert">{{ error }}</p>
    <p v-else-if="message" class="mt-4 text-xl" role="status">{{ message }}</p>
  </Card>
</template>
