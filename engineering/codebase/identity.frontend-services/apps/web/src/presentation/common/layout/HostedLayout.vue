<script setup lang="ts">
import { Card } from '@wow-two-beta/fe/presentation/display';
import { computed } from 'vue';
import type { SignInOptions } from '../../../domain/account';

/**
 * Frames a hosted page in the instance's look: its fe sdk theme by name, its background image (a same-origin path or an
 * HTTPS URL) or the theme's own backdrop, and one opaque card that stays readable over either.
 */
defineOptions({ name: 'HostedLayout' });

const props = defineProps<{
  /** The look and brand to show; the default theme while they load. */
  options: SignInOptions | null;
  /** The page's heading. */
  title: string;
}>();

defineSlots<{
  /** The page's content inside the card. */
  default?(): unknown;
  /** The links under the content, inside the card. */
  footer?(): unknown;
}>();

/** A registry theme id as a class name; anything else falls back to the document's theme. */
const themeClass = computed(() => {
  const theme = props.options?.theme ?? '';
  return /^[a-z0-9-]+$/.test(theme) ? `theme-${theme}` : '';
});

/** Whether `url` is a path on this origin or an absolute HTTPS URL, the two the service accepts in its settings. */
function isBackgroundUrl(url: string): boolean {
  if (url.startsWith('/')) return !url.startsWith('//') && !url.includes('\\');
  try {
    return new URL(url).protocol === 'https:';
  } catch {
    return false;
  }
}

/** A same-origin or HTTPS image only; anything else keeps the theme's own backdrop. */
const backgroundImage = computed(() => {
  const url = props.options?.backgroundImageUrl;
  return url && isBackgroundUrl(url) ? `url("${encodeURI(url).replace(/"/g, '%22')}")` : null;
});
</script>

<template>
  <div
    :class="[
      themeClass,
      'min-h-dvh bg-background font-sans text-foreground',
      backgroundImage ? 'bg-cover bg-fixed bg-center' : 'surface-ambient',
    ]"
    :style="backgroundImage ? { backgroundImage } : undefined"
  >
    <main class="mx-auto flex min-h-dvh w-full max-w-md flex-col justify-center px-4 py-10">
      <Card class="flex flex-col gap-6 p-6 shadow-xl sm:p-8">
        <header class="flex flex-col gap-1">
          <p class="text-sm font-medium text-muted-foreground">{{ options?.brandName ?? 'Identity' }}</p>
          <h1 class="text-2xl font-semibold">{{ title }}</h1>
        </header>
        <slot></slot>
        <footer v-if="$slots.footer" class="flex flex-col gap-2 border-t border-border pt-4 text-sm">
          <slot name="footer"></slot>
        </footer>
      </Card>
    </main>
  </div>
</template>
