<script setup lang="ts">
import { computed } from 'vue';

/**
 * Shows the console's brand in the top bar: a monogram of the instance's brand name, the name itself and a "Console"
 * tag. Identity has no icon yet (`product/brand/brand.md`), so the monogram stands in for one.
 */
defineOptions({ name: 'ConsoleBrand' });
const props = withDefaults(
  defineProps<{
    /** The instance's brand name, from its sign-in declaration. */
    brandName?: string;
    /** Shows the monogram alone, where the name does not fit. */
    isCompact?: boolean;
  }>(),
  { brandName: 'Identity', isCompact: false },
);

const initial = computed(() => props.brandName.trim().charAt(0).toUpperCase() || 'I');
</script>

<template>
  <span class="inline-flex min-w-0 items-center gap-2.5">
    <span
      aria-hidden="true"
      class="grid size-7 shrink-0 place-items-center rounded-lg bg-primary text-sm font-semibold text-primary-foreground"
    >
      {{ initial }}
    </span>
    <span :class="props.isCompact ? 'sr-only' : 'truncate text-sm font-semibold text-foreground'">
      {{ props.brandName }}
    </span>
    <span
      v-if="!props.isCompact"
      class="shrink-0 rounded-md bg-muted px-1.5 py-0.5 text-xs font-medium text-muted-foreground"
    >
      Console
    </span>
  </span>
</template>
