<script setup lang="ts">
/** Lays out one hosted page in the card of `HostedLayout`: its heading, its content and the links under them. */
defineOptions({ name: 'HostedPage' });

defineProps<{
  /** The page's heading. */
  title: string;
}>();

defineSlots<{
  /** The page's content. */
  default?(): unknown;
  /** The links under the content. */
  footer?(): unknown;
}>();
</script>

<template>
  <div class="flex flex-col gap-6">
    <h1 class="text-2xl font-semibold">{{ title }}</h1>
    <slot></slot>
    <footer v-if="$slots.footer" class="flex flex-col gap-2 border-t border-border pt-4 text-sm">
      <slot name="footer"></slot>
    </footer>
  </div>
</template>
